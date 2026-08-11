namespace PortsideApi.Data;

/// <summary>Raw-SQL access to the UserPreference table.</summary>
public sealed class UserPreferenceStore
{
    private readonly SqliteConnectionFactory _db;

    public UserPreferenceStore(SqliteConnectionFactory db) => _db = db;

    public async Task<string?> GetJsonAsync(Guid userId)
    {
        using var conn = await _db.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT PreferencesJson FROM "UserPreference" WHERE UserId = $id LIMIT 1""";
        cmd.Parameters.AddWithValue("$id", userId.ToString());
        return await cmd.ExecuteScalarAsync() as string;
    }

    public async Task UpsertJsonAsync(Guid userId, string json)
    {
        using var conn = await _db.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO "UserPreference" (UserId, PreferencesJson, UpdatedAt) VALUES ($id, $j, $t)
            ON CONFLICT(UserId) DO UPDATE SET PreferencesJson = $j, UpdatedAt = $t
            """;
        cmd.Parameters.AddWithValue("$id", userId.ToString());
        cmd.Parameters.AddWithValue("$j", json);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }
}
