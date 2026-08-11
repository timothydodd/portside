using k8s;
using k8s.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PortsideApi.Common;
using PortsideApi.Models;
using PortsideApi.Services;

namespace PortsideApi.Endpoints;

public static class KubernetesEndpoints
{
    public static void MapKubernetesEndpoints(this WebApplication app)
    {
        var logger = app.Logger;
        var group = app.MapGroup("/api/kubernetes");

        group.MapGet("/metrics", async (IMemoryCache cache, KubernetesService kubernetesService) =>
        {
            try
            {
                if (!cache.TryGetValue(KubernetesService.MetricsCacheKey, out Cluster? cachedMetrics) || cachedMetrics is null)
                    return Results.Ok(await kubernetesService.GetMetrics());
                return Results.Ok(cachedMetrics);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get cluster metrics");
                return Results.Json(new ErrorResponse("Failed to get cluster metrics", ex.Message), AppJsonContext.Default.ErrorResponse, statusCode: 500);
            }
        });

        group.MapGet("/nodes", async (IKubernetes client) =>
        {
            try
            {
                var nodes = await client.CoreV1.ListNodeAsync();
                return Results.Ok(nodes.Items.StripManagedFields().ToList());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get nodes");
                return Results.Json(new ErrorResponse("Failed to get nodes", ex.Message), AppJsonContext.Default.ErrorResponse, statusCode: 500);
            }
        });

        group.MapGet("/pods", async (IKubernetes client, PodMonitorService podMonitor,
            [FromQuery(Name = "namespace")] string? ns = null) =>
        {
            try
            {
                V1PodList pods = string.IsNullOrEmpty(ns)
                    ? await client.CoreV1.ListPodForAllNamespacesAsync()
                    : await client.CoreV1.ListNamespacedPodAsync(ns);
                pods.Items.StripManagedFields();

                try
                {
                    var podMetrics = string.IsNullOrEmpty(ns)
                        ? await client.GetPodMetricsAsync()
                        : await client.GetPodMetricsByNamespaceAsync(ns);

                    // Node capacity for percentage calculations
                    var nodes = await client.CoreV1.ListNodeAsync();
                    var nodeCapacityDict = new Dictionary<string, (double cpuCores, double memoryBytes)>();
                    foreach (var node in nodes.Items)
                    {
                        double cpuCapacity = 0;
                        double memoryCapacity = 0;
                        if (node.Status.Capacity.TryGetValue("cpu", out var cpu))
                            double.TryParse(cpu.ToString(), out cpuCapacity);
                        if (node.Status.Capacity.TryGetValue("memory", out var memory))
                            memoryCapacity = KubernetesService.ParseMemory(memory.ToString());
                        nodeCapacityDict[node.Metadata.Name] = (cpuCapacity, memoryCapacity);
                    }

                    var metricsDict = new Dictionary<string, (double cpuMilli, double memBytes)>();
                    foreach (var metric in podMetrics.Items)
                    {
                        var key = $"{metric.Metadata.Namespace}/{metric.Metadata.Name}";
                        double totalCpu = 0;
                        double totalMemory = 0;
                        foreach (var container in metric.Containers)
                        {
                            if (container.Usage.TryGetValue("cpu", out var cpuVal))
                            {
                                try { totalCpu += KubernetesService.ParseCpu(cpuVal); }
                                catch (Exception cpuEx)
                                {
                                    logger.LogWarning("Failed to parse CPU for pod {Pod}: {Value} - {Message}",
                                        metric.Metadata.Name, cpuVal, cpuEx.Message);
                                }
                            }
                            if (container.Usage.TryGetValue("memory", out var mem))
                            {
                                try { totalMemory += KubernetesService.ParseMemory(mem); }
                                catch (Exception memEx)
                                {
                                    logger.LogWarning("Failed to parse memory for pod {Pod}: {Value} - {Message}",
                                        metric.Metadata.Name, mem, memEx.Message);
                                }
                            }
                        }
                        metricsDict[key] = (totalCpu * 1000, totalMemory);
                    }

                    // Enhance pod objects with metrics, plus monitor-cached counts + CPU history.
                    var cachedMetrics = podMonitor.CurrentMetrics;
                    var cachedCounts = podMonitor.CurrentCounts;

                    var enhancedPods = pods.Items.Select(pod =>
                    {
                        var podKey = $"{pod.Metadata.NamespaceProperty}/{pod.Metadata.Name}";
                        PodMetricsDto? metrics = null;

                        // Prefer the monitor's cached metrics (already includes percentages),
                        // fall back to a fresh metrics-server read so things work even when
                        // monitoring is disabled.
                        if (cachedMetrics.TryGetValue(podKey, out var cached))
                        {
                            metrics = new PodMetricsDto(
                                cached.Cpu, cached.Memory, cached.CpuPercent, cached.MemoryPercent,
                                podMonitor.GetCpuHistoryDownsampled(podKey, 30));
                        }
                        else if (metricsDict.TryGetValue(podKey, out var podMetric))
                        {
                            var nodeName = pod.Spec?.NodeName;
                            double? cpuPercent = null;
                            double? memoryPercent = null;

                            if (!string.IsNullOrEmpty(nodeName) && nodeCapacityDict.TryGetValue(nodeName, out var nodeCapacity))
                            {
                                if (nodeCapacity.cpuCores > 0)
                                    cpuPercent = Math.Round((podMetric.cpuMilli / 1000 / nodeCapacity.cpuCores) * 100, 1);
                                if (nodeCapacity.memoryBytes > 0)
                                    memoryPercent = Math.Round((podMetric.memBytes / nodeCapacity.memoryBytes) * 100, 1);
                            }

                            metrics = new PodMetricsDto(
                                podMetric.cpuMilli, podMetric.memBytes, cpuPercent, memoryPercent,
                                podMonitor.GetCpuHistoryDownsampled(podKey, 30));
                        }

                        PodCountsDto? counts = null;
                        if (cachedCounts.TryGetValue(podKey, out var cnt))
                        {
                            counts = new PodCountsDto(cnt.Error, cnt.Warning);
                        }

                        return new EnhancedPod(pod.Metadata, pod.Spec, pod.Status, metrics, counts);
                    }).ToList();

                    return Results.Ok(enhancedPods);
                }
                catch (Exception metricsEx)
                {
                    logger.LogWarning(metricsEx, "Failed to get pod metrics, returning pods without metrics");
                    return Results.Ok(pods.Items.ToList());
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get pods for namespace {Namespace}", ns ?? "all");
                return Results.Json(new ErrorResponse("Failed to get pods", ex.Message), AppJsonContext.Default.ErrorResponse, statusCode: 500);
            }
        });

        group.MapDelete("/pods/{ns}/{name}", async (string ns, string name, IKubernetes client) =>
        {
            try
            {
                await client.CoreV1.DeleteNamespacedPodAsync(name, ns);
                return Results.Ok(new MessageResponse($"Pod {name} deleted successfully"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete pod {Name} in namespace {Namespace}", name, ns);
                return Results.Json(new ErrorResponse("Failed to delete pod", ex.Message), AppJsonContext.Default.ErrorResponse, statusCode: 500);
            }
        });

        group.MapGet("/deployments", async (IKubernetes client, [FromQuery(Name = "namespace")] string? ns = null) =>
        {
            try
            {
                V1DeploymentList deployments = string.IsNullOrEmpty(ns)
                    ? await client.AppsV1.ListDeploymentForAllNamespacesAsync()
                    : await client.AppsV1.ListNamespacedDeploymentAsync(ns);
                return Results.Ok(deployments.Items.StripManagedFields().ToList());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get deployments for namespace {Namespace}", ns ?? "all");
                return Results.Json(new ErrorResponse("Failed to get deployments", ex.Message), AppJsonContext.Default.ErrorResponse, statusCode: 500);
            }
        });

        group.MapPatch("/deployments/{ns}/{name}/scale", async (string ns, string name, ScaleRequest request, IKubernetes client) =>
        {
            try
            {
                var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(name, ns);
                deployment.Spec.Replicas = request.Replicas;

                await client.AppsV1.ReplaceNamespacedDeploymentAsync(deployment, name, ns);
                return Results.Ok(new MessageResponse($"Deployment {name} scaled to {request.Replicas} replicas"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to scale deployment {Name} in namespace {Namespace}", name, ns);
                return Results.Json(new ErrorResponse("Failed to scale deployment", ex.Message), AppJsonContext.Default.ErrorResponse, statusCode: 500);
            }
        });

        group.MapGet("/services", async (IKubernetes client, [FromQuery(Name = "namespace")] string? ns = null) =>
        {
            try
            {
                V1ServiceList services = string.IsNullOrEmpty(ns)
                    ? await client.CoreV1.ListServiceForAllNamespacesAsync()
                    : await client.CoreV1.ListNamespacedServiceAsync(ns);
                return Results.Ok(services.Items.StripManagedFields().ToList());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get services for namespace {Namespace}", ns ?? "all");
                return Results.Json(new ErrorResponse("Failed to get services", ex.Message), AppJsonContext.Default.ErrorResponse, statusCode: 500);
            }
        });

        group.MapGet("/namespaces", async (IKubernetes client) =>
        {
            try
            {
                var namespaces = await client.CoreV1.ListNamespaceAsync();
                return Results.Ok(namespaces.Items.StripManagedFields().ToList());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get namespaces");
                return Results.Json(new ErrorResponse("Failed to get namespaces", ex.Message), AppJsonContext.Default.ErrorResponse, statusCode: 500);
            }
        });

        group.MapGet("/events", async (IKubernetes client, [FromQuery(Name = "namespace")] string? ns = null) =>
        {
            try
            {
                Corev1EventList events = string.IsNullOrEmpty(ns)
                    ? await client.CoreV1.ListEventForAllNamespacesAsync()
                    : await client.CoreV1.ListNamespacedEventAsync(ns);
                return Results.Ok(events.Items.StripManagedFields().ToList());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get events for namespace {Namespace}", ns ?? "all");
                return Results.Json(new ErrorResponse("Failed to get events", ex.Message), AppJsonContext.Default.ErrorResponse, statusCode: 500);
            }
        });
    }
}
