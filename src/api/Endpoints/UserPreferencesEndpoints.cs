using System.Security.Claims;
using System.Text.Json;
using PortsideApi.Data;
using PortsideApi.Models;
using PortsideApi.Services;

namespace PortsideApi.Endpoints;

public static class UserPreferencesEndpoints
{
    public static void MapUserPreferencesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/user/preferences").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, AuthService auth, UserPreferenceStore prefs) =>
        {
            var userId = auth.GetUserIdFromPrincipal(principal);
            if (!userId.HasValue) return Results.Unauthorized();

            var json = await prefs.GetJsonAsync(userId.Value);
            return Results.Content(json ?? "{}", "application/json");
        });

        group.MapPut("/", async (JsonElement body, ClaimsPrincipal principal, AuthService auth, UserPreferenceStore prefs) =>
        {
            var userId = auth.GetUserIdFromPrincipal(principal);
            if (!userId.HasValue) return Results.Unauthorized();

            var json = body.GetRawText();
            if (string.IsNullOrWhiteSpace(json)) json = "{}";

            // Cap payload to avoid unbounded growth
            if (json.Length > 64 * 1024)
                return Results.BadRequest(new ErrorResponse("Preferences payload too large"));

            await prefs.UpsertJsonAsync(userId.Value, json);
            return Results.Content(json, "application/json");
        });
    }
}
