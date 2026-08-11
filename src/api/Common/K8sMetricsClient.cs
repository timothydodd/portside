using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using k8s;
using PortsideApi.Models;

namespace PortsideApi.Common;

/// <summary>
/// Replacement for the GetKubernetesNodesMetricsAsync/GetKubernetesPodsMetricsAsync
/// extensions that KubernetesClient.Aot omits. The non-generic CustomObjects calls
/// return the payload as a JsonElement; we deserialize it through AppJsonContext so
/// the whole path is source-generated and AOT-safe.
/// </summary>
public static class K8sMetricsClient
{
    private const string Group = "metrics.k8s.io";
    private const string Version = "v1beta1";

    public static async Task<NodeMetricsListDto> GetNodeMetricsAsync(this IKubernetes client, CancellationToken ct = default)
    {
        var raw = await client.CustomObjects.ListClusterCustomObjectAsync(Group, Version, "nodes", cancellationToken: ct);
        return Materialize(raw, AppJsonContext.Default.NodeMetricsListDto);
    }

    public static async Task<PodMetricsListDto> GetPodMetricsAsync(this IKubernetes client, CancellationToken ct = default)
    {
        var raw = await client.CustomObjects.ListClusterCustomObjectAsync(Group, Version, "pods", cancellationToken: ct);
        return Materialize(raw, AppJsonContext.Default.PodMetricsListDto);
    }

    public static async Task<PodMetricsListDto> GetPodMetricsByNamespaceAsync(this IKubernetes client, string ns, CancellationToken ct = default)
    {
        var raw = await client.CustomObjects.ListNamespacedCustomObjectAsync(Group, Version, ns, "pods", cancellationToken: ct);
        return Materialize(raw, AppJsonContext.Default.PodMetricsListDto);
    }

    private static T Materialize<T>(object? raw, JsonTypeInfo<T> typeInfo) => raw switch
    {
        JsonElement el => el.Deserialize(typeInfo)
            ?? throw new InvalidOperationException("Empty metrics payload"),
        JsonDocument doc => doc.RootElement.Deserialize(typeInfo)
            ?? throw new InvalidOperationException("Empty metrics payload"),
        string s => JsonSerializer.Deserialize(s, typeInfo)
            ?? throw new InvalidOperationException("Empty metrics payload"),
        _ => throw new InvalidOperationException(
            $"Unexpected metrics payload type: {raw?.GetType().Name ?? "null"}"),
    };
}
