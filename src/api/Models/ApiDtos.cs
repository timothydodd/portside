using System.Text.Json.Serialization;
using k8s.Models;

namespace PortsideApi.Models;

// Named DTOs replacing the anonymous types MVC used to serialize by reflection.
// Every type here (and everything reachable from it) is registered in AppJsonContext
// so serialization is source-generated and Native AOT safe.

public record ErrorResponse(string Error, string? Message = null);
public record MessageResponse(string Message);

// SignalR watch payloads
public record PodWatchEvent(string EventType, V1Pod Pod);
public record NodeWatchEvent(string EventType, V1Node Node);
public record K8sEventWatchEvent(string EventType, Corev1Event Event);

// /api/kubernetes/pods
public record PodMetricsDto(double Cpu, double Memory, double? CpuPercent, double? MemoryPercent, double[] History);
public record PodCountsDto(int Error, int Warning);
public record EnhancedPod(V1ObjectMeta Metadata, V1PodSpec? Spec, V1PodStatus? Status, PodMetricsDto? Metrics, PodCountsDto? Counts);

public record ScaleRequest(int Replicas);

// /api/log
public record LogPodInfo(string Name, string Deployment, string Namespace, string LogLevel, string[] Containers);
public record LogLinesResponse(List<string> Lines);
public record LogCountsResponse(
    int Error,
    int Warning,
    int SinceSeconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Unavailable = null);

// /api/auth
public class LoginRequest
{
    public required string UserName { get; set; }
    public required string Password { get; set; }
}

public class ChangePasswordRequest
{
    public required string OldPassword { get; set; }
    public required string NewPassword { get; set; }
}

public class LoginResponse
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
}

public class UserResponse
{
    public Guid Id { get; set; }
    public required string UserName { get; set; }
}

public class RefreshTokenRequest
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
}

public class RevokeTokenRequest
{
    public required string RefreshToken { get; set; }
}

// Global exception middleware payload
public record ErrorDetail(string Message, int StatusCode, DateTime Timestamp);
public record ErrorEnvelope(ErrorDetail Error);

// Health endpoint payload
public record HealthEntryDto(string Status, string? Description, double Duration,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error);
public record HealthReportDto(string Status, double TotalDuration, Dictionary<string, HealthEntryDto> Entries);
