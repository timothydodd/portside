using System.Collections.Concurrent;
using k8s;
using Microsoft.AspNetCore.SignalR;
using PortsideApi.Common;
using PortsideApi.Hubs;

namespace PortsideApi.Services;

public sealed record PodLogLine(
    long Id,
    string Pod,
    string Namespace,
    string Container,
    string Line,
    string LogLevel,
    DateTime TimeStamp,
    long SequenceNumber);

public sealed class PodLogStreamService : IAsyncDisposable
{
    private readonly IKubernetes _kubernetes;
    private readonly IHubContext<PodLogHub> _hub;
    private readonly ILogger<PodLogStreamService> _logger;
    private readonly ConcurrentDictionary<string, PodStream> _streams = new();
    private long _sequence;

    public PodLogStreamService(IKubernetes kubernetes, IHubContext<PodLogHub> hub, ILogger<PodLogStreamService> logger)
    {
        _kubernetes = kubernetes;
        _hub = hub;
        _logger = logger;
    }

    private static string Key(string ns, string pod) => $"{ns}/{pod}";

    public Task SubscribeAsync(string connectionId, string ns, string pod)
    {
        var key = Key(ns, pod);
        var stream = _streams.GetOrAdd(key, _ => StartPodStream(ns, pod));
        stream.Subscribers.TryAdd(connectionId, 0);
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(string connectionId, string ns, string pod)
    {
        var key = Key(ns, pod);
        if (_streams.TryGetValue(key, out var stream))
        {
            stream.Subscribers.TryRemove(connectionId, out _);
            if (stream.Subscribers.IsEmpty)
            {
                stream.Cts.Cancel();
                _streams.TryRemove(key, out _);
            }
        }
        return Task.CompletedTask;
    }

    public void RemoveAllForConnection(string connectionId)
    {
        foreach (var (key, stream) in _streams)
        {
            if (stream.Subscribers.TryRemove(connectionId, out _) && stream.Subscribers.IsEmpty)
            {
                stream.Cts.Cancel();
                _streams.TryRemove(key, out _);
            }
        }
    }

    private PodStream StartPodStream(string ns, string pod)
    {
        var cts = new CancellationTokenSource();
        var stream = new PodStream(cts);

        _ = Task.Run(async () =>
        {
            try
            {
                var podObj = await _kubernetes.CoreV1.ReadNamespacedPodAsync(pod, ns, cancellationToken: cts.Token);
                var containers = podObj.Spec?.Containers?.Select(c => c.Name).ToArray()
                                 ?? Array.Empty<string>();
                if (containers.Length == 0)
                {
                    _logger.LogWarning("Pod {Ns}/{Pod} has no containers", ns, pod);
                    return;
                }

                var tasks = containers.Select(c => StreamContainer(ns, pod, c, cts.Token)).ToArray();
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pod log stream ended for {Ns}/{Pod}", ns, pod);
            }
            finally
            {
                _streams.TryRemove(Key(ns, pod), out _);
            }
        }, cts.Token);

        return stream;
    }

    private async Task StreamContainer(string ns, string pod, string container, CancellationToken token)
    {
        try
        {
            using var resp = await _kubernetes.CoreV1.ReadNamespacedPodLogWithHttpMessagesAsync(
                name: pod,
                namespaceParameter: ns,
                container: container,
                follow: true,
                timestamps: true,
                tailLines: 200,
                cancellationToken: token);

            using var reader = new StreamReader(resp.Body);
            var groupName = $"pod-{ns}-{pod}";
            string? line;
            while (!token.IsCancellationRequested && (line = await reader.ReadLineAsync(token)) != null)
            {
                var entry = ParseLine(ns, pod, container, line);
                await _hub.Clients.Group(groupName).SendAsync("ReceiveLog", new[] { entry }, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Container log stream ended for {Ns}/{Pod}/{Container}", ns, pod, container);
        }
    }

    private PodLogLine ParseLine(string ns, string pod, string container, string raw)
    {
        var ts = DateTime.UtcNow;
        var content = raw;
        var spaceIdx = raw.IndexOf(' ');
        if (spaceIdx > 0 && DateTime.TryParse(raw.Substring(0, spaceIdx), out var parsed))
        {
            ts = parsed.ToUniversalTime();
            content = raw.Substring(spaceIdx + 1);
        }

        // Detect level from the raw content before cleaning strips "[ERROR]" style prefixes.
        var level = LogLineCleaner.DetectLevel(content);

        // Strip ANSI codes and leading "13:24:54 info:" style prefixes for display.
        content = LogLineCleaner.Clean(content);

        return new PodLogLine(
            Id: Interlocked.Increment(ref _sequence),
            Pod: pod,
            Namespace: ns,
            Container: container,
            Line: content,
            LogLevel: level,
            TimeStamp: ts,
            SequenceNumber: _sequence);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (_, stream) in _streams)
        {
            stream.Cts.Cancel();
        }
        _streams.Clear();
        await Task.CompletedTask;
    }

    private sealed class PodStream
    {
        public CancellationTokenSource Cts { get; }
        public ConcurrentDictionary<string, byte> Subscribers { get; } = new();
        public PodStream(CancellationTokenSource cts) => Cts = cts;
    }
}
