using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PortsideApi.Data.Models;

namespace PortsideApi.Services;

public class AuthService
{
    private readonly IConfiguration _configuration;
    private readonly PasswordService _passwordService;

    public AuthService(IConfiguration configuration, PasswordService passwordService)
    {
        _configuration = configuration;
        _passwordService = passwordService;
    }

    public string GenerateJwtToken(User user)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT secret not configured");
        var issuer = jwtSettings["Issuer"];
        var audience = jwtSettings["Audience"];
        var expiryMinutes = int.TryParse(jwtSettings["ExpiryMinutes"], out var m) ? m : 60;

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        // JsonWebTokenHandler instead of JwtSecurityTokenHandler: same wire format,
        // but trim/AOT compatible (no reflection-based claim mapping on the write path).
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes),
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.UserName,
                ["user-id"] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public bool ValidateUser(User user, string password)
        => _passwordService.VerifyPassword(user, password);

    public async Task<ClaimsPrincipal?> GetPrincipalFromExpiredTokenAsync(string token)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT secret not configured");

        var parameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ValidateLifetime = false
        };

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token, parameters);
        if (!result.IsValid) return null;

        if (result.SecurityToken is not JsonWebToken jwt ||
            !jwt.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
        {
            return null;
        }

        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    public Guid? GetUserIdFromPrincipal(ClaimsPrincipal principal)
    {
        var id = principal.Claims.FirstOrDefault(c => c.Type == "user-id")?.Value;
        return Guid.TryParse(id, out var g) ? g : null;
    }
}
