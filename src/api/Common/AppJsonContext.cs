using System.Text.Json;
using System.Text.Json.Serialization;
using k8s.Models;
using PortsideApi.Models;
using PortsideApi.Services;

namespace PortsideApi.Common;

/// <summary>
/// Source-generated JSON metadata for every payload the API serializes over HTTP or
/// SignalR. Required for Native AOT — reflection-based System.Text.Json is unavailable.
/// CamelCase naming matches what the MVC serializer produced before the AOT conversion,
/// and the K8s model types carry their own [JsonPropertyName]/[JsonConverter] attributes,
/// so the wire format is unchanged.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    // Metadata-only: the fast-path serialization generator mis-emits handlers for
    // converter-attributed types like ResourceQuantity (unresolved type-info refs).
    GenerationMode = JsonSourceGenerationMode.Metadata,
    // The upstream converters for these types are internal to KubernetesClient.Aot,
    // so we register accessible equivalents (see K8sValueConverters).
    Converters = new[] { typeof(AppResourceQuantityConverter), typeof(AppIntOrStringConverter) })]
// Simple app DTOs
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(MessageResponse))]
[JsonSerializable(typeof(ErrorEnvelope))]
[JsonSerializable(typeof(HealthReportDto))]
// Auth
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(UserResponse))]
[JsonSerializable(typeof(RefreshTokenRequest))]
[JsonSerializable(typeof(RevokeTokenRequest))]
// Cluster metrics + monitor settings
[JsonSerializable(typeof(Cluster))]
[JsonSerializable(typeof(MonitorSettings))]
[JsonSerializable(typeof(Dictionary<string, PodLogCounts>))]
[JsonSerializable(typeof(Dictionary<string, PodResourceUsage>))]
// Metrics fetched via the CustomObjects API (metrics.k8s.io)
[JsonSerializable(typeof(NodeMetricsListDto))]
[JsonSerializable(typeof(PodMetricsListDto))]
// Watch-stream frames (K8sWatch) deserialize single objects
[JsonSerializable(typeof(V1Pod))]
[JsonSerializable(typeof(V1Node))]
[JsonSerializable(typeof(Corev1Event))]
// Kubernetes resource lists returned by the API
[JsonSerializable(typeof(List<V1Node>))]
[JsonSerializable(typeof(List<V1Pod>))]
[JsonSerializable(typeof(List<V1Deployment>))]
[JsonSerializable(typeof(List<V1Service>))]
[JsonSerializable(typeof(List<V1Namespace>))]
[JsonSerializable(typeof(List<Corev1Event>))]
[JsonSerializable(typeof(List<EnhancedPod>))]
[JsonSerializable(typeof(ScaleRequest))]
// SignalR payloads
[JsonSerializable(typeof(PodWatchEvent))]
[JsonSerializable(typeof(NodeWatchEvent))]
[JsonSerializable(typeof(K8sEventWatchEvent))]
[JsonSerializable(typeof(PodLogLine[]))]
[JsonSerializable(typeof(string))]
// Pod log endpoints
[JsonSerializable(typeof(List<LogPodInfo>))]
[JsonSerializable(typeof(LogLinesResponse))]
[JsonSerializable(typeof(LogCountsResponse))]
// User preferences passthrough body
[JsonSerializable(typeof(JsonElement))]
public partial class AppJsonContext : JsonSerializerContext
{
}
