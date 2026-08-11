using PortsideApi.Services.Interfaces;
using PortsideApi.Models;
using PortsideApi.Common;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Caching.Memory;

namespace PortsideApi.Services;

public class KubernetesService : IKubernetesService
{
    private readonly IKubernetes _kubernetesClient;
    private readonly IMemoryCache _memoryCache;
    public static string NodeCacheKey = Constants.CacheKeys.Nodes;
    public static string MetricsCacheKey = Constants.CacheKeys.Metrics;
    public KubernetesService(IKubernetes kubernetesClient, IMemoryCache memoryCache)
    {
        _kubernetesClient = kubernetesClient;
        _memoryCache = memoryCache;
    }
    public async Task WatchMetrics(Func<string, V1Node, Task> onEvent,
            Action<Exception>? onError = null,
            Action? onClosed = null,
            CancellationToken token = default)
    {
        var responseTask = _kubernetesClient.CoreV1.ListNodeWithHttpMessagesAsync(
            watch: true, cancellationToken: token);
        await K8sWatch.WatchAsync(responseTask, AppJson.V1Node, onEvent, onError, token);
        onClosed?.Invoke();
    }

    private async Task<V1NodeList?> GetNodes()
    {

        try
        {
            if (_memoryCache.TryGetValue(NodeCacheKey, out V1NodeList? nodes))
            {
                if (nodes != null)
                    return nodes;
            }

            var nodes2 = await _kubernetesClient.CoreV1.ListNodeAsync();
            nodes2.Items.StripManagedFields();

            _memoryCache.Set(NodeCacheKey, nodes2, TimeSpan.FromMinutes(5));
            return nodes2;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error fetching nodes: {e.Message}");
        }
        return null;
    }
    public async Task<Cluster> GetMetrics()
    {



        var r = await GetNodes();
        if (r == null)
            throw new Exception("Failed to get nodes");

        var nodeMap = new Dictionary<string, NodeStats>();



        // Fetch node details
        var metrics = await _kubernetesClient.GetNodeMetricsAsync();

        double cpuTotal = 0;
        double memTotal = 0;
        double cpuUsageTotal = 0;
        double memoryUsageTotal = 0;
        foreach (var item in r.Items)
        {
            var n = new NodeStats() { Name = item.Metadata.Name };


            if (item.Status.Capacity.TryGetValue("cpu", out var cpuCapacity))
            {
                n.CpuTotal = double.Parse(cpuCapacity?.ToString() ?? "0");
                cpuTotal += n.CpuTotal ?? 0;
            }
            if (item.Status.Capacity.TryGetValue("memory", out var memoryCap))
            {
                var mem = memoryCap?.ToString();
                if (!string.IsNullOrEmpty(mem))
                {
                    n.MemoryTotal = ParseMemory(mem);
                    memTotal += n.MemoryTotal ?? 0;
                }
            }
            nodeMap.Add(n.Name, n);



        }
        foreach (var m in metrics.Items)
        {
            if (nodeMap.TryGetValue(m.Metadata.Name, out var node))
            {
                if (node == null)
                    continue;

                if (m.Usage.TryGetValue("cpu", out var cpuRq))
                {

                    var cpu = ParseCpu(cpuRq ?? "0");
                    cpuUsageTotal += cpu;
                    node.CpuLoad = cpu;
                    node.CpuPercentage = (double)(cpu / (node.CpuTotal ?? cpu) * 100);

                }
                if (m.Usage.TryGetValue("memory", out var memRq))
                {

                    var memoryUsage = ParseMemory(memRq ?? "0");
                    memoryUsageTotal += memoryUsage;
                    node.MemoryUsage = memoryUsage;

                    node.MemoryPercentage = (double)(memoryUsage / (node.MemoryTotal ?? memoryUsage) * 100);

                }
            }


        }
        return new Cluster()
        {
            CpuTotal = cpuTotal,
            MemoryTotal = memTotal,
            CpuUsage = cpuUsageTotal,
            MemoryUsage = memoryUsageTotal,
            CpuPercentage = (cpuUsageTotal / cpuTotal) * 100,
            MemoryPercentage = (memoryUsageTotal / memTotal) * 100,
            Nodes = nodeMap.Select(x => x.Value).ToList()
        };
    }
    public static double ParseCpu(string cpuStr)
    {
        if (string.IsNullOrEmpty(cpuStr))
            throw new ArgumentException("CPU string is null or empty", nameof(cpuStr));

        // Try parsing as a plain decimal number first (like "0.001", "1.5")
        if (double.TryParse(cpuStr, out double plainValue))
        {
            return plainValue;
        }

        // Handle unit suffixes
        if (cpuStr.Length < 2)
            throw new ArgumentException("Invalid CPU string format", nameof(cpuStr));

        // Check for nanoseconds (n)
        if (cpuStr.EndsWith("n"))
        {
            if (double.TryParse(cpuStr.Substring(0, cpuStr.Length - 1), out double nanoValue))
            {
                return nanoValue / 1_000_000_000.0;
            }
        }
        // Check for microseconds (u)
        else if (cpuStr.EndsWith("u"))
        {
            if (double.TryParse(cpuStr.Substring(0, cpuStr.Length - 1), out double microValue))
            {
                return microValue / 1_000_000.0;
            }
        }
        // Check for millicores (m)
        else if (cpuStr.EndsWith("m"))
        {
            if (double.TryParse(cpuStr.Substring(0, cpuStr.Length - 1), out double milliValue))
            {
                return milliValue / 1_000.0;
            }
        }
        // Try parsing as a whole number (cores)
        else if (int.TryParse(cpuStr, out int coreValue))
        {
            return coreValue;
        }

        throw new ArgumentException($"Invalid CPU string format: {cpuStr}", nameof(cpuStr));
    }

    public static double ParseMemory(string memStr)
    {
        int unitLength = memStr.EndsWith("i") ? 2 : 1;
        if (double.TryParse(memStr.Substring(0, memStr.Length - unitLength), out double baseValue))
        {
            string units = memStr.Substring(memStr.Length - unitLength);
            if (!int.TryParse(units, out _))
            {
                switch (units)
                {
                    case "Ki":
                        return baseValue * 1000;
                    case "K":
                        return baseValue * 1024;
                    case "Mi":
                        return baseValue * 1_000_000;
                    case "M":
                        return baseValue * 1024 * 1024;
                    case "Gi":
                        return baseValue * 1_000_000_000;
                    case "G":
                        return baseValue * 1024 * 1024 * 1024;
                    default:
                        return baseValue;
                }
            }
            else
            {
                return int.Parse(memStr);
            }
        }
        throw new ArgumentException("Invalid memory string format", nameof(memStr));
    }
}

