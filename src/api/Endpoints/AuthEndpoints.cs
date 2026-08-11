using System.Security.Claims;
using System.Text.RegularExpressions;
using PortsideApi.Common;
using PortsideApi.Data;
using PortsideApi.Models;
using PortsideApi.Services;

namespace PortsideApi.Endpoints;

public static partial class AuthEndpoints
{
    [GeneratedRegex(@"^[a-zA-Z0-9_@.-]+$")]
    private static partial Regex UserNameRegex();

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").RequireAuthorization();

        group.MapPost("/login", async (LoginRequest request, UserStore users, AuthService auth,
            RefreshTokenService refreshTokens, IConfiguration config) =>
        {
            if (request.UserName is not { Length: >= 3 and <= 100 } || !UserNameRegex().IsMatch(request.UserName)
                || request.Password is not { Length: >= 1 and <= 256 })
            {
                return Results.BadRequest(new ErrorResponse("Invalid username or password"));
            }

            try
            {
                var user = await users.GetByUserNameAsync(request.UserName);
                if (user == null || !auth.ValidateUser(user, request.Password))
                    return Results.Json(new ErrorResponse("Invalid username or password"), AppJsonContext.Default.ErrorResponse, statusCode: 401);

                var token = auth.GenerateJwtToken(user);
                var refresh = await refreshTokens.CreateRefreshTokenAsync(user.Id);

                return Results.Ok(new LoginResponse
                {
                    AccessToken = token,
                    RefreshToken = refresh.Token,
                    ExpiresIn = ExpirySeconds(config)
                });
            }
            catch
            {
                return Results.Json(new ErrorResponse("An error occurred during authentication"), AppJsonContext.Default.ErrorResponse, statusCode: 500);
            }
        }).AllowAnonymous().RequireRateLimiting("AuthPolicy");

        group.MapGet("/user", async (ClaimsPrincipal principal, UserStore users) =>
        {
            var userName = GetUserName(principal);
            if (userName == null) return Results.Unauthorized();

            var user = await users.GetByUserNameAsync(userName);
            if (user == null) return Results.NotFound();

            return Results.Ok(new UserResponse { Id = user.Id, UserName = user.UserName });
        });

        group.MapPost("/change-password", async (ChangePasswordRequest request, ClaimsPrincipal principal,
            UserStore users, AuthService auth, PasswordService passwords) =>
        {
            if (request.OldPassword is not { Length: >= 1 and <= 256 }
                || request.NewPassword is not { Length: >= 8 and <= 256 })
            {
                return Results.BadRequest(new ErrorResponse("New password must be at least 8 characters"));
            }

            var userName = GetUserName(principal);
            if (userName == null) return Results.Json(new ErrorResponse("User not authenticated"), AppJsonContext.Default.ErrorResponse, statusCode: 401);

            var user = await users.GetByUserNameAsync(userName);
            if (user == null) return Results.NotFound(new ErrorResponse("User not found"));
            if (!auth.ValidateUser(user, request.OldPassword))
                return Results.Json(new ErrorResponse("Current password is incorrect"), AppJsonContext.Default.ErrorResponse, statusCode: 401);

            var newHash = passwords.HashPassword(user, request.NewPassword);
            await users.UpdatePasswordHashAsync(user.Id, newHash);
            return Results.Ok(new MessageResponse("Password changed successfully"));
        }).RequireRateLimiting("AuthPolicy");

        group.MapPost("/refresh", async (RefreshTokenRequest request, UserStore users, AuthService auth,
            RefreshTokenService refreshTokens, IConfiguration config) =>
        {
            var principal = await auth.GetPrincipalFromExpiredTokenAsync(request.AccessToken);
            if (principal == null) return Results.BadRequest(new ErrorResponse("Invalid access token"));

            var userId = auth.GetUserIdFromPrincipal(principal);
            if (!userId.HasValue) return Results.BadRequest(new ErrorResponse("Invalid access token"));

            if (!await refreshTokens.ValidateRefreshTokenAsync(request.RefreshToken))
                return Results.Json(new ErrorResponse("Invalid refresh token"), AppJsonContext.Default.ErrorResponse, statusCode: 401);

            var user = await users.GetByIdAsync(userId.Value);
            if (user == null) return Results.NotFound(new ErrorResponse("User not found"));

            var newRefresh = await refreshTokens.RotateRefreshTokenAsync(request.RefreshToken, user.Id);
            var newAccess = auth.GenerateJwtToken(user);

            return Results.Ok(new LoginResponse
            {
                AccessToken = newAccess,
                RefreshToken = newRefresh.Token,
                ExpiresIn = ExpirySeconds(config)
            });
        }).AllowAnonymous();

        group.MapPost("/revoke", async (RevokeTokenRequest request, RefreshTokenService refreshTokens) =>
        {
            await refreshTokens.RevokeRefreshTokenAsync(request.RefreshToken);
            return Results.Ok(new MessageResponse("Token revoked successfully"));
        });

        group.MapPost("/logout", async (ClaimsPrincipal principal, AuthService auth, RefreshTokenService refreshTokens) =>
        {
            var userId = auth.GetUserIdFromPrincipal(principal);
            if (userId.HasValue)
                await refreshTokens.RevokeAllUserRefreshTokensAsync(userId.Value);
            return Results.Ok(new MessageResponse("Logged out successfully"));
        });
    }

    private static string? GetUserName(ClaimsPrincipal principal)
        => principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
           ?? principal.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

    private static int ExpirySeconds(IConfiguration config)
        => (int.TryParse(config["JwtSettings:ExpiryMinutes"], out var m) ? m : 60) * 60;
}
