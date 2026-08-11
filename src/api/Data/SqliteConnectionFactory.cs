using Microsoft.Data.Sqlite;

namespace PortsideApi.Data;

/// <summary>
/// Hands out open SQLite connections. Replaces the OrmLite/Dapper factory so the data
/// layer is reflection-free and Native AOT compatible.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string connectionString) => _connectionString = connectionString;

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
