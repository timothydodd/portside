namespace PortsideApi.Models;

// Lightweight replacements for the NodeMetrics/PodMetrics models, which (like the
// metrics extension methods) are excluded from KubernetesClient.Aot. Fetched via the
// CustomObjects API from metrics.k8s.io/v1beta1. Usage values stay raw quantity
// strings ("231m", "123456Ki") and are parsed with KubernetesService.ParseCpu/Memory.

public sealed class MetricsObjectMeta
{
    public string Name { get; set; } = string.Empty;
    public string? Namespace { get; set; }
}

public sealed class NodeMetricsDto
{
    public MetricsObjectMeta Metadata { get; set; } = new();
    public Dictionary<string, string> Usage { get; set; } = new();
}

public sealed class NodeMetricsListDto
{
    public List<NodeMetricsDto> Items { get; set; } = new();
}

public sealed class ContainerMetricsDto
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Usage { get; set; } = new();
}

public sealed class PodMetricsItemDto
{
    public MetricsObjectMeta Metadata { get; set; } = new();
    public List<ContainerMetricsDto> Containers { get; set; } = new();
}

public sealed class PodMetricsListDto
{
    public List<PodMetricsItemDto> Items { get; set; } = new();
}
