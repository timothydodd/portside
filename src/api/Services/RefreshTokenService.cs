using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PortsideApi.Data;
using PortsideApi.Data.Models;

namespace PortsideApi.Services;

public class RefreshTokenService
{
    private readonly SqliteConnectionFactory _dbFactory;
    private readonly IConfiguration _configuration;

    public RefreshTokenService(SqliteConnectionFactory dbFactory, IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _configuration = configuration;
    }

    public async Task<RefreshToken> CreateRefreshTokenAsync(Guid userId)
    {
        var token = GenerateRefreshToken();
        var expiryDays = int.TryParse(_configuration["JwtSettings:RefreshTokenExpiryDays"], out var d) ? d : 30;

        var refreshToken = new RefreshToken
        {
            Token = token,
            UserId = userId,
            ExpiryDate = DateTime.UtcNow.AddDays(expiryDays),
            IsRevoked = false,
            CreatedDate = DateTime.UtcNow
        };

        using var conn = await _dbFactory.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO "RefreshToken" (Token, UserId, ExpiryDate, IsRevoked, CreatedDate)
            VALUES ($token, $userId, $expiry, 0, $created);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$token", refreshToken.Token);
        cmd.Parameters.AddWithValue("$userId", refreshToken.UserId.ToString());
        cmd.Parameters.AddWithValue("$expiry", refreshToken.ExpiryDate.ToString("O"));
        cmd.Parameters.AddWithValue("$created", refreshToken.CreatedDate.ToString("O"));
        refreshToken.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return refreshToken;
    }

    public async Task<RefreshToken?> GetRefreshTokenAsync(string token)
    {
        using var conn = await _dbFactory.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Token, UserId, ExpiryDate, IsRevoked, CreatedDate
            FROM "RefreshToken" WHERE Token = $token AND IsRevoked = 0 LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$token", token);
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<bool> ValidateRefreshTokenAsync(string token)
    {
        var rt = await GetRefreshTokenAsync(token);
        return rt != null && rt.ExpiryDate > DateTime.UtcNow;
    }

    public async Task RevokeRefreshTokenAsync(string token)
    {
        using var conn = await _dbFactory.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """UPDATE "RefreshToken" SET IsRevoked = 1 WHERE Token = $token""";
        cmd.Parameters.AddWithValue("$token", token);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RevokeAllUserRefreshTokensAsync(Guid userId)
    {
        using var conn = await _dbFactory.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """UPDATE "RefreshToken" SET IsRevoked = 1 WHERE UserId = $userId AND IsRevoked = 0""";
        cmd.Parameters.AddWithValue("$userId", userId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<RefreshToken> RotateRefreshTokenAsync(string oldToken, Guid userId)
    {
        await RevokeRefreshTokenAsync(oldToken);
        return await CreateRefreshTokenAsync(userId);
    }

    private static RefreshToken Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Token = r.GetString(1),
        UserId = Guid.Parse(r.GetString(2)),
        ExpiryDate = DateTime.Parse(r.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
        IsRevoked = r.GetInt64(4) != 0,
        CreatedDate = DateTime.Parse(r.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
    };

    private static string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}
