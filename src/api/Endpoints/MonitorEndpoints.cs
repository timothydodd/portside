using PortsideApi.Services;

namespace PortsideApi.Endpoints;

public static class MonitorEndpoints
{
    public static void MapMonitorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monitor").RequireAuthorization();

        group.MapGet("/settings", (MonitorSettingsService settings) => Results.Ok(settings.Get()));

        group.MapPut("/settings", async (MonitorSettings settings, MonitorSettingsService service) =>
        {
            var saved = await service.Save(settings);
            return Results.Ok(saved);
        });

        group.MapGet("/counts", (PodMonitorService monitor) =>
            Results.Ok(monitor.CurrentCounts.ToDictionary(kv => kv.Key, kv => kv.Value)));

        group.MapGet("/metrics", (PodMonitorService monitor) =>
            Results.Ok(monitor.CurrentMetrics.ToDictionary(kv => kv.Key, kv => kv.Value)));
    }
}
