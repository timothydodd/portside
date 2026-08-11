using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PortsideApi.Models;

namespace PortsideApi.Common.HealthChecks;

public static class HealthCheckResponseWriter
{
    public static Task Write(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new HealthReportDto(
            report.Status.ToString(),
            report.TotalDuration.TotalMilliseconds,
            report.Entries.ToDictionary(
                kv => kv.Key,
                kv => new HealthEntryDto(
                    kv.Value.Status.ToString(),
                    kv.Value.Description,
                    kv.Value.Duration.TotalMilliseconds,
                    kv.Value.Exception?.Message)));
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, AppJsonContext.Default.HealthReportDto));
    }
}
