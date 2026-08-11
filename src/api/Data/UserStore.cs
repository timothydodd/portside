using Microsoft.Data.Sqlite;
using PortsideApi.Data.Models;

namespace PortsideApi.Data;

/// <summary>Raw-SQL access to the User table (AOT-safe, no reflection mapping).</summary>
public sealed class UserStore
{
    private readonly SqliteConnectionFactory _db;

    public UserStore(SqliteConnectionFactory db) => _db = db;

    public async Task<User?> GetByUserNameAsync(string userName)
    {
        using var conn = await _db.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT Id, UserName, PasswordHash, TimeStamp FROM "User" WHERE UserName = $u LIMIT 1""";
        cmd.Parameters.AddWithValue("$u", userName);
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT Id, UserName, PasswordHash, TimeStamp FROM "User" WHERE Id = $id LIMIT 1""";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task UpdatePasswordHashAsync(Guid id, string passwordHash)
    {
        using var conn = await _db.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """UPDATE "User" SET PasswordHash = $p WHERE Id = $id""";
        cmd.Parameters.AddWithValue("$p", passwordHash);
        cmd.Parameters.AddWithValue("$id", id.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public void Insert(SqliteConnection conn, User user)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """INSERT INTO "User" (Id, UserName, PasswordHash, TimeStamp) VALUES ($id, $u, $p, $t)""";
        cmd.Parameters.AddWithValue("$id", user.Id.ToString());
        cmd.Parameters.AddWithValue("$u", user.UserName);
        cmd.Parameters.AddWithValue("$p", user.PasswordHash);
        cmd.Parameters.AddWithValue("$t", user.TimeStamp.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static User Map(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(0)),
        UserName = r.GetString(1),
        PasswordHash = r.GetString(2),
        TimeStamp = DateTime.Parse(r.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
    };
}
