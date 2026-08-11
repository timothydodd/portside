using System.Collections.Concurrent;
using k8s;
using PortsideApi.Common;

namespace PortsideApi.Services;

public record PodLogCounts(int Error, int Warning, DateTime UpdatedAt);

public record PodResourceUsage(double Cpu, double Memory, double? CpuPercent, double? MemoryPercent, DateTime UpdatedAt);

public record PodResourceSample(DateTime At, double CpuPercent, double MemoryBytes);

/// <summary>
/// Periodically refreshes 24h log error/warning counts and CPU/memory metrics for all
/// non-excluded pods and caches them in-memory. Consumers (e.g. KubernetesController.GetPods)
/// merge the cached values into their responses on demand — no live push.
/// </summary>
public sealed class PodMonitorService : IAsyncDisposable
{
    private readonly IKubernetes _kubernetes;
    private readonly MonitorSettingsService _settings;
    private readonly ILogger<PodMonitorService> _logger;

    private readonly ConcurrentDictionary<string, PodLogCounts> _counts = new();
    private readonly ConcurrentDictionary<string, PodResourceUsage> _metrics = new();
    private readonly ConcurrentDictionary<string, string[]> _containers = new();
    private readonly ConcurrentDictionary<string, Queue<PodResourceSample>> _history = new();
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(24);

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _logTask;
    private Task? _metricsTask;
    private int _started;

    public PodMonitorService(
        IKubernetes kubernetes,
        MonitorSettingsService settings,
        ILogger<PodMonitorService> logger)
    {
        _kubernetes = kubernetes;
        _settings = settings;
        _logger = logger;

        _settings.Changed += OnSettingsChanged;
    }

    public IReadOnlyDictionary<string, PodLogCounts> CurrentCounts => _counts;
    public IReadOnlyDictionary<string, PodResourceUsage> CurrentMetrics => _metrics;

    public double[] GetCpuHistory(string key)
    {
        if (!_history.TryGetValue(key, out var q)) return Array.Empty<double>();
        lock (q) return q.Select(s => s.CpuPercent).ToArray();
    }

    public double[] GetMemoryHistory(string key)
    {
        if (!_history.TryGetValue(key, out var q)) return Array.Empty<double>();
        lock (q) return q.Select(s => s.MemoryBytes).ToArray();
    }

    public PodResourceSample[] GetHistory(string key)
    {
        if (!_history.TryGetValue(key, out var q)) return Array.Empty<PodResourceSample>();
        lock (q) return q.ToArray();
    }

    /// <summary>
    /// Returns the CPU% history bucketed into <paramref name="buckets"/> evenly-spaced
    /// time slots covering the full 24h window. Each bucket is the mean of samples that
    /// fall within it; empty buckets are dropped from the trailing end so a freshly
    /// observed pod doesn't render as a long flat line.
    /// </summary>
    public double[] GetCpuHistoryDownsampled(string key, int buckets)
    {
        if (buckets <= 0) return Array.Empty<double>();
        PodResourceSample[] samples;
        if (!_history.TryGetValue(key, out var q)) return Array.Empty<double>();
        lock (q) samples = q.ToArray();
        if (samples.Length == 0) return Array.Empty<double>();
        if (samples.Length <= buckets) return samples.Select(s => s.CpuPercent).ToArray();

        var now = DateTime.UtcNow;
        var start = now - HistoryWindow;
        var bucketSpan = HistoryWindow.TotalSeconds / buckets;

        var sums = new double[buckets];
        var counts = new int[buckets];
        foreach (var s in samples)
        {
            var offset = (s.At - start).TotalSeconds;
            var idx = (int)Math.Floor(offset / bucketSpan);
            if (idx < 0) idx = 0;
            if (idx >= buckets) idx = buckets - 1;
            sums[idx] += s.CpuPercent;
            counts[idx]++;
        }

        var result = new List<double>(buckets);
        double lastSeen = 0;
        bool seenAny = false;
        for (int i = 0; i < buckets; i++)
        {
            if (counts[i] > 0)
            {
                lastSeen = sums[i] / counts[i];
                seenAny = true;
                result.Add(lastSeen);
            }
            else if (seenAny)
            {
                // Carry the prior value forward so gaps don't dip to zero.
                result.Add(lastSeen);
            }
        }
        return result.ToArray();
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started > 0)
            {
                _started++;
                return;
            }
            _started = 1;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _logTask = Task.Run(() => LogScanLoop(token), token);
            _metricsTask = Task.Run(() => MetricsLoop(token), token);
            _logger.LogInformation("PodMonitorService started");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_started == 0) return;
            _started--;
            if (_started > 0) return;
            // Cancel without Dispose — the scan/poll loops still hold this token
            // (see ClusterWatchManager.StopWatchers_Locked for rationale).
            _cts?.Cancel();
            _cts = null;
            _logTask = null;
            _metricsTask = null;
            _logger.LogInformation("PodMonitorService stopped");
        }
    }

    private void OnSettingsChanged(MonitorSettings s)
    {
        if (!s.Enabled)
        {
            _counts.Clear();
            _metrics.Clear();
            _history.Clear();
            return;
        }
        // Drop cached entries for newly-excluded pods so stale data doesn't linger.
        var excluded = new HashSet<string>(s.ExcludedPods);
        foreach (var key in _counts.Keys.Where(excluded.Contains).ToList()) _counts.TryRemove(key, out _);
        foreach (var key in _metrics.Keys.Where(excluded.Contains).ToList()) _metrics.TryRemove(key, out _);
        foreach (var key in _history.Keys.Where(excluded.Contains).ToList()) _history.TryRemove(key, out _);
    }

    private async Task LogScanLoop(CancellationToken token)
    {
        // small initial delay so we don't compete with the first metrics scan
        try { await Task.Delay(TimeSpan.FromSeconds(5), token); }
        catch (OperationCanceledException) { return; }

        while (!token.IsCancellationRequested)
        {
            var settings = _settings.Get();
            if (settings.Enabled)
            {
                try { await ScanLogsOnce(settings, token); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.LogWarning(ex, "Log scan iteration failed"); }
            }

            var delaySeconds = settings.Enabled ? settings.LogScanIntervalSeconds : 30;
            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task MetricsLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var settings = _settings.Get();
            if (settings.Enabled)
            {
                try { await PollMetricsOnce(settings, token); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.LogWarning(ex, "Metrics poll iteration failed"); }
            }

            var delaySeconds = settings.Enabled ? settings.MetricsPollIntervalSeconds : 30;
            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ScanLogsOnce(MonitorSettings settings, CancellationToken token)
    {
        var pods = await _kubernetes.CoreV1.ListPodForAllNamespacesAsync(cancellationToken: token);
        var excluded = new HashSet<string>(settings.ExcludedPods);
        var window = settings.LogWindowSeconds;

        foreach (var pod in pods.Items)
        {
            if (token.IsCancellationRequested) break;
            var ns = pod.Metadata.NamespaceProperty;
            var name = pod.Metadata.Name;
            var key = $"{ns}/{name}";
            if (excluded.Contains(key)) continue;
            var phase = pod.Status?.Phase;
            if (phase != "Running" && phase != "Failed") continue;

            var containers = pod.Spec?.Containers?.Select(c => c.Name).ToArray() ?? Array.Empty<string>();
            _containers[key] = containers;

            int errors = 0, warnings = 0;
            foreach (var c in containers)
            {
                try
                {
                    using var resp = await _kubernetes.CoreV1.ReadNamespacedPodLogWithHttpMessagesAsync(
                        name: name,
                        namespaceParameter: ns,
                        container: c,
                        follow: false,
                        timestamps: false,
                        sinceSeconds: window,
                        cancellationToken: token);
                    using var reader = new StreamReader(resp.Body);
                    // Stream line-by-line: a 24h log window can be many MB, and buffering
                    // it into one string allocates on the large object heap every scan.
                    string? line;
                    while ((line = await reader.ReadLineAsync(token)) != null)
                    {
                        if (line.Length == 0) continue;
                        if (LogLineCleaner.HasErrorMarker(line)) errors++;
                        else if (LogLineCleaner.HasWarningMarker(line)) warnings++;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (k8s.Autorest.HttpOperationException) { /* skip — container may not have logs yet */ }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Log scan failed for {Key} container {Container}", key, c);
                }
            }

            _counts[key] = new PodLogCounts(errors, warnings, DateTime.UtcNow);
        }

        // Drop entries for pods that no longer exist.
        var alive = new HashSet<string>(pods.Items.Select(p => $"{p.Metadata.NamespaceProperty}/{p.Metadata.Name}"));
        foreach (var stale in _counts.Keys.Where(k => !alive.Contains(k)).ToList()) _counts.TryRemove(stale, out _);
        foreach (var stale in _containers.Keys.Where(k => !alive.Contains(k)).ToList()) _containers.TryRemove(stale, out _);
    }

    private async Task PollMetricsOnce(MonitorSettings settings, CancellationToken token)
    {
        var excluded = new HashSet<string>(settings.ExcludedPods);

        var pods = await _kubernetes.CoreV1.ListPodForAllNamespacesAsync(cancellationToken: token);
        var nodes = await _kubernetes.CoreV1.ListNodeAsync(cancellationToken: token);
        var nodeCapacity = new Dictionary<string, (double Cpu, double Memory)>();
        foreach (var n in nodes.Items)
        {
            double cpu = 0, mem = 0;
            if (n.Status?.Capacity?.TryGetValue("cpu", out var c) == true)
                double.TryParse(c.ToString(), out cpu);
            if (n.Status?.Capacity?.TryGetValue("memory", out var m) == true)
                mem = KubernetesService.ParseMemory(m.ToString());
            nodeCapacity[n.Metadata.Name] = (cpu, mem);
        }

        Dictionary<string, (double cpuMilli, double memBytes)> usage = new();
        try
        {
            var metrics = await _kubernetes.GetPodMetricsAsync(token);
            foreach (var m in metrics.Items)
            {
                double cpuCores = 0, mem = 0;
                foreach (var c in m.Containers)
                {
                    if (c.Usage.TryGetValue("cpu", out var cpuVal))
                    {
                        try { cpuCores += KubernetesService.ParseCpu(cpuVal); } catch { }
                    }
                    if (c.Usage.TryGetValue("memory", out var memVal))
                    {
                        try { mem += KubernetesService.ParseMemory(memVal); } catch { }
                    }
                }
                usage[$"{m.Metadata.Namespace}/{m.Metadata.Name}"] = (cpuCores * 1000.0, mem);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "metrics.k8s.io unavailable; skipping pod metrics this cycle");
            return;
        }

        foreach (var pod in pods.Items)
        {
            var ns = pod.Metadata.NamespaceProperty;
            var name = pod.Metadata.Name;
            var key = $"{ns}/{name}";
            if (excluded.Contains(key)) continue;
            if (!usage.TryGetValue(key, out var u)) continue;

            double? cpuPct = null, memPct = null;
            var nodeName = pod.Spec?.NodeName;
            if (!string.IsNullOrEmpty(nodeName) && nodeCapacity.TryGetValue(nodeName, out var cap))
            {
                if (cap.Cpu > 0) cpuPct = Math.Round((u.cpuMilli / 1000.0) / cap.Cpu * 100.0, 1);
                if (cap.Memory > 0) memPct = Math.Round(u.memBytes / cap.Memory * 100.0, 1);
            }

            var now = DateTime.UtcNow;
            _metrics[key] = new PodResourceUsage(u.cpuMilli, u.memBytes, cpuPct, memPct, now);

            if (cpuPct.HasValue || u.memBytes > 0)
            {
                var queue = _history.GetOrAdd(key, _ => new Queue<PodResourceSample>());
                var cutoff = now - HistoryWindow;
                lock (queue)
                {
                    queue.Enqueue(new PodResourceSample(now, cpuPct ?? 0, u.memBytes));
                    while (queue.Count > 0 && queue.Peek().At < cutoff) queue.Dequeue();
                }
            }
        }

        var alive = new HashSet<string>(pods.Items.Select(p => $"{p.Metadata.NamespaceProperty}/{p.Metadata.Name}"));
        foreach (var stale in _metrics.Keys.Where(k => !alive.Contains(k)).ToList()) _metrics.TryRemove(stale, out _);
        foreach (var stale in _history.Keys.Where(k => !alive.Contains(k)).ToList()) _history.TryRemove(stale, out _);
    }

    public ValueTask DisposeAsync()
    {
        _settings.Changed -= OnSettingsChanged;
        lock (_gate)
        {
            _started = 0;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        return ValueTask.CompletedTask;
    }
}
