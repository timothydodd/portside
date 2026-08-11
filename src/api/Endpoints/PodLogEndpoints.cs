using k8s;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PortsideApi.Common;
using PortsideApi.Models;

namespace PortsideApi.Endpoints;

public static class PodLogEndpoints
{
    public static void MapPodLogEndpoints(this WebApplication app)
    {
        var logger = app.Logger;
        var group = app.MapGroup("/api/log").RequireAuthorization();

        group.MapGet("/pods", async (IKubernetes kubernetes) =>
        {
            var pods = await kubernetes.CoreV1.ListPodForAllNamespacesAsync();
            var result = pods.Items.Select(p => new LogPodInfo(
                Name: p.Metadata.Name,
                Deployment: p.Metadata.OwnerReferences?.FirstOrDefault()?.Name ?? p.Metadata.Name,
                Namespace: p.Metadata.NamespaceProperty,
                LogLevel: p.Status?.Phase ?? "Unknown",
                Containers: p.Spec?.Containers?.Select(c => c.Name).ToArray() ?? Array.Empty<string>()
            )).ToList();
            return Results.Ok(result);
        });

        group.MapGet("/tail", async (
            [FromQuery(Name = "namespace")] string ns,
            [FromQuery] string pod,
            IKubernetes kubernetes,
            IMemoryCache cache,
            CancellationToken ct,
            [FromQuery] string? container = null,
            [FromQuery] int tailLines = 500,
            [FromQuery] int? sinceSeconds = null) =>
        {
            try
            {
                var containers = container is { Length: > 0 }
                    ? new[] { container }
                    : await GetContainerNames(kubernetes, cache, ns, pod);

                var lines = new List<string>();
                foreach (var c in containers)
                {
                    using var resp = await kubernetes.CoreV1.ReadNamespacedPodLogWithHttpMessagesAsync(
                        name: pod,
                        namespaceParameter: ns,
                        container: c,
                        follow: false,
                        timestamps: true,
                        tailLines: sinceSeconds.HasValue ? null : tailLines,
                        sinceSeconds: sinceSeconds,
                        cancellationToken: ct);
                    using var reader = new StreamReader(resp.Body);
                    string? rawLine;
                    while ((rawLine = await reader.ReadLineAsync(ct)) != null)
                    {
                        if (rawLine.Length == 0) continue;
                        // Preserve "<timestamp> " prefix (frontend strips it for display)
                        // but clean inline level/time/ANSI noise from the message body.
                        var spaceIdx = rawLine.IndexOf(' ');
                        if (spaceIdx > 0)
                        {
                            var ts = rawLine.Substring(0, spaceIdx);
                            var body = LogLineCleaner.Clean(rawLine.Substring(spaceIdx + 1));
                            lines.Add($"{ts} {body}");
                        }
                        else
                        {
                            lines.Add(LogLineCleaner.Clean(rawLine));
                        }
                    }
                }
                return Results.Ok(new LogLinesResponse(lines));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch logs for {Ns}/{Pod}", ns, pod);
                return Results.Json(new ErrorResponse(ex.Message), AppJsonContext.Default.ErrorResponse, statusCode: 500);
            }
        });

        group.MapGet("/counts", async (
            [FromQuery(Name = "namespace")] string ns,
            [FromQuery] string pod,
            IKubernetes kubernetes,
            IMemoryCache cache,
            CancellationToken ct,
            [FromQuery] int sinceSeconds = 86400) =>
        {
            var key = $"logcounts:{ns}/{pod}:{sinceSeconds}";
            if (cache.TryGetValue(key, out LogCountsResponse? cached) && cached is not null)
            {
                return Results.Ok(cached);
            }

            try
            {
                var containers = await GetContainerNames(kubernetes, cache, ns, pod);
                int errors = 0, warnings = 0;

                foreach (var c in containers)
                {
                    try
                    {
                        using var resp = await kubernetes.CoreV1.ReadNamespacedPodLogWithHttpMessagesAsync(
                            name: pod,
                            namespaceParameter: ns,
                            container: c,
                            follow: false,
                            timestamps: false,
                            sinceSeconds: sinceSeconds,
                            cancellationToken: ct);

                        using var reader = new StreamReader(resp.Body);
                        string? line;
                        while ((line = await reader.ReadLineAsync(ct)) != null)
                        {
                            if (line.Length == 0) continue;
                            if (LogLineCleaner.HasErrorMarker(line)) errors++;
                            else if (LogLineCleaner.HasWarningMarker(line)) warnings++;
                        }
                    }
                    catch (k8s.Autorest.HttpOperationException)
                    {
                        // Container may not have logs yet (just-started, init), skip silently.
                    }
                }

                var result = new LogCountsResponse(errors, warnings, sinceSeconds);
                cache.Set(key, result, TimeSpan.FromSeconds(60));
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                // Cache the unavailable result too so we don't hammer K8s.
                var fallback = new LogCountsResponse(0, 0, sinceSeconds, Unavailable: true);
                cache.Set(key, fallback, TimeSpan.FromMinutes(5));
                logger.LogWarning("Log count fetch failed for {Ns}/{Pod}: {Message}", ns, pod, ex.Message);
                return Results.Ok(fallback);
            }
        });
    }

    private static async Task<string[]> GetContainerNames(IKubernetes kubernetes, IMemoryCache cache, string ns, string pod)
    {
        var key = $"containers:{ns}/{pod}";
        if (cache.TryGetValue(key, out string[]? cached) && cached is not null)
        {
            return cached;
        }
        var p = await kubernetes.CoreV1.ReadNamespacedPodAsync(pod, ns);
        var names = p.Spec?.Containers?.Select(c => c.Name).ToArray() ?? Array.Empty<string>();
        cache.Set(key, names, TimeSpan.FromMinutes(5));
        return names;
    }
}
