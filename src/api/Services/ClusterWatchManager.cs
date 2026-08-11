using k8s;
using k8s.Models;
using PortsideApi.Common;
using PortsideApi.Hubs;
using PortsideApi.Models;
using Microsoft.Extensions.Caching.Memory;

namespace PortsideApi.Services;

/// <summary>
/// Reference-counted owner of the cluster-wide K8s watch streams (nodes, pods, events) and
/// the periodic metrics poll. Watchers start when the first SignalR client connects to the
/// dashboard hub and stop when the last one disconnects, so we only consume cluster API
/// quota while a UI is actually attached.
/// </summary>
public sealed class ClusterWatchManager : IAsyncDisposable
{
    private readonly KubernetesService _kubernetesService;
    private readonly IMemoryCache _memoryCache;
    private readonly KubernetesDashboardHubService _dashboardHubService;
    private readonly IKubernetes _kubernetesClient;
    private readonly PodMonitorService _podMonitor;
    private readonly ILogger<ClusterWatchManager> _logger;

    private readonly object _gate = new();
    private int _subscribers;
    private CancellationTokenSource? _cts;
    private Task? _metricsTask;
    private Task? _watchNodesTask;
    private Task? _watchPodsTask;
    private Task? _watchEventsTask;

    public ClusterWatchManager(
        KubernetesService kubernetesService,
        IMemoryCache memoryCache,
        KubernetesDashboardHubService dashboardHubService,
        IKubernetes kubernetesClient,
        PodMonitorService podMonitor,
        ILogger<ClusterWatchManager> logger)
    {
        _kubernetesService = kubernetesService;
        _memoryCache = memoryCache;
        _dashboardHubService = dashboardHubService;
        _kubernetesClient = kubernetesClient;
        _podMonitor = podMonitor;
        _logger = logger;
    }

    public void AddSubscriber()
    {
        lock (_gate)
        {
            _subscribers++;
            if (_subscribers == 1)
            {
                _logger.LogInformation("First dashboard subscriber connected; starting cluster watchers");
                StartWatchers_Locked();
            }
        }
    }

    public void RemoveSubscriber()
    {
        lock (_gate)
        {
            if (_subscribers == 0) return;
            _subscribers--;
            if (_subscribers == 0)
            {
                _logger.LogInformation("Last dashboard subscriber disconnected; stopping cluster watchers");
                StopWatchers_Locked();
            }
        }
    }

    private void StartWatchers_Locked()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _metricsTask = Task.Run(() => MetricsLoop(token), token);
        _watchNodesTask = Task.Run(() => WatchNodes(token), token);
        _watchPodsTask = Task.Run(() => WatchPods(token), token);
        _watchEventsTask = Task.Run(() => WatchEvents(token), token);
        _podMonitor.Start();
    }

    private void StopWatchers_Locked()
    {
        // Cancel but don't Dispose: the watch tasks still hold this token, and
        // disposing while they're mid-await turns clean cancellation into
        // ObjectDisposedException / "stream was not readable" noise. A CTS with no
        // timer holds no resources worth reclaiming eagerly.
        _cts?.Cancel();
        _cts = null;
        _metricsTask = _watchNodesTask = _watchPodsTask = _watchEventsTask = null;
        _podMonitor.Stop();
    }

    private async Task MetricsLoop(CancellationToken stoppingToken)
    {
        var consecutiveFailures = 0;
        var lastLoggedFailure = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(30);
            try
            {
                var metrics = await _kubernetesService.GetMetrics();
                _memoryCache.Set(KubernetesService.MetricsCacheKey, metrics, TimeSpan.FromMinutes(1));
                await _dashboardHubService.SendClusterUpdate(Constants.DefaultCluster.Id, metrics);
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (IsTransientMetricsError(ex))
            {
                consecutiveFailures++;
                delay = TimeSpan.FromSeconds(Math.Min(300, 30 * consecutiveFailures));
                if (consecutiveFailures == 1 || (DateTime.UtcNow - lastLoggedFailure).TotalMinutes >= 5)
                {
                    _logger.LogWarning(
                        "Cluster metrics unavailable (transient): {Message}. Backing off to {Delay}s. (failure #{Count})",
                        ex.Message, delay.TotalSeconds, consecutiveFailures);
                    lastLoggedFailure = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                _logger.LogError(ex, "Error updating cluster metrics");
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task WatchNodes(CancellationToken token)
    {
        try
        {
            await _kubernetesService.WatchMetrics(
                onEvent: async (eventType, node) =>
                {
                    _logger.LogDebug("Node event: {EventType} - {Name}", eventType, node.Metadata.Name);
                    node.StripManagedFields();
                    await _dashboardHubService.SendNodeUpdate(Constants.DefaultCluster.Id, new NodeWatchEvent(eventType, node));
                },
                onError: ex => _logger.LogWarning(ex, "Node watch error"),
                onClosed: () => _logger.LogInformation("Node watch closed"),
                token: token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start node watch");
        }
    }

    private async Task WatchPods(CancellationToken token)
    {
        try
        {
            var responseTask = _kubernetesClient.CoreV1.ListPodForAllNamespacesWithHttpMessagesAsync(
                watch: true, cancellationToken: token);
            await K8sWatch.WatchAsync(responseTask, AppJson.V1Pod,
                onEvent: async (eventType, pod) =>
                {
                    _logger.LogDebug("Pod event: {EventType} - {Name} in {Ns}",
                        eventType, pod.Metadata.Name, pod.Metadata.NamespaceProperty);
                    pod.StripManagedFields();
                    var podEvent = new PodWatchEvent(eventType, pod);
                    await _dashboardHubService.SendPodUpdate(Constants.DefaultCluster.Id, pod.Metadata.NamespaceProperty, podEvent);
                    await _dashboardHubService.SendClusterPodUpdate(Constants.DefaultCluster.Id, podEvent);
                },
                onError: ex => _logger.LogWarning(ex, "Pod watch error"),
                token: token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start pod watch");
        }
    }

    private async Task WatchEvents(CancellationToken token)
    {
        try
        {
            var responseTask = _kubernetesClient.CoreV1.ListEventForAllNamespacesWithHttpMessagesAsync(
                watch: true, cancellationToken: token);
            await K8sWatch.WatchAsync(responseTask, AppJson.Corev1Event,
                onEvent: async (eventType, k8sEvent) =>
                {
                    _logger.LogDebug("K8s event: {EventType} - {Message}", eventType, k8sEvent.Message);
                    k8sEvent.StripManagedFields();
                    await _dashboardHubService.SendEventUpdate(Constants.DefaultCluster.Id,
                        new K8sEventWatchEvent(eventType, k8sEvent));
                },
                onError: ex => _logger.LogWarning(ex, "Event watch error"),
                token: token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start event watch");
        }
    }

    private static bool IsTransientMetricsError(Exception ex)
        => ex is System.Net.Http.HttpRequestException
        || ex is System.Net.Http.HttpIOException
        || ex is TaskCanceledException
        || ex is k8s.Autorest.HttpOperationException;

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _subscribers = 0;
            StopWatchers_Locked();
        }
        return ValueTask.CompletedTask;
    }
}
