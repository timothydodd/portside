namespace PortsideApi.Data;

/// <summary>Raw-SQL access to the SystemSetting key/value table.</summary>
public sealed class SystemSettingStore
{
    private readonly SqliteConnectionFactory _db;

    public SystemSettingStore(SqliteConnectionFactory db) => _db = db;

    public string? GetJson(string key)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT ValueJson FROM "SystemSetting" WHERE Key = $k LIMIT 1""";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public async Task UpsertJsonAsync(string key, string json)
    {
        using var conn = await _db.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO "SystemSetting" (Key, ValueJson, UpdatedAt) VALUES ($k, $j, $t)
            ON CONFLICT(Key) DO UPDATE SET ValueJson = $j, UpdatedAt = $t
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$j", json);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }
}
