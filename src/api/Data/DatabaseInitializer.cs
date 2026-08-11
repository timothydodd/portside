using PortsideApi.Data.Models;
using PortsideApi.Services;

namespace PortsideApi.Data;

public class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _dbFactory;
    private readonly UserStore _users;
    private readonly PasswordService _passwordService;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(SqliteConnectionFactory dbFactory, UserStore users, PasswordService passwordService, ILogger<DatabaseInitializer> logger)
    {
        _dbFactory = dbFactory;
        _users = users;
        _passwordService = passwordService;
        _logger = logger;
    }

    public void CreateTable()
    {
        using var conn = _dbFactory.Open();

        bool userTableExisted;
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'User'";
            userTableExisted = Convert.ToInt64(check.ExecuteScalar()) > 0;
        }

        using (var ddl = conn.CreateCommand())
        {
            ddl.CommandText = """
                CREATE TABLE IF NOT EXISTS "User" (
                    Id TEXT NOT NULL PRIMARY KEY,
                    UserName TEXT NOT NULL,
                    PasswordHash TEXT NOT NULL,
                    TimeStamp TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_User_UserName ON "User" (UserName);

                CREATE TABLE IF NOT EXISTS "RefreshToken" (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Token TEXT NOT NULL,
                    UserId TEXT NOT NULL,
                    ExpiryDate TEXT NOT NULL,
                    IsRevoked INTEGER NOT NULL,
                    CreatedDate TEXT NOT NULL,
                    FOREIGN KEY (UserId) REFERENCES "User"(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS "UserPreference" (
                    UserId TEXT NOT NULL PRIMARY KEY,
                    PreferencesJson TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS "SystemSetting" (
                    Key TEXT NOT NULL PRIMARY KEY,
                    ValueJson TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                """;
            ddl.ExecuteNonQuery();
        }

        // Databases created by the old Dapper/OrmLite layer stored Guids as UPPERCASE
        // text; the ADO.NET layer binds lowercase Guid.ToString(). SQLite compares FK
        // text case-sensitively, so normalize once (idempotent) or refresh-token
        // inserts fail with "FOREIGN KEY constraint failed".
        using (var normalize = conn.CreateCommand())
        {
            normalize.CommandText = """
                PRAGMA foreign_keys = OFF;
                UPDATE "User" SET Id = lower(Id) WHERE Id <> lower(Id);
                UPDATE "RefreshToken" SET UserId = lower(UserId) WHERE UserId <> lower(UserId);
                UPDATE "UserPreference" SET UserId = lower(UserId) WHERE UserId <> lower(UserId);
                PRAGMA foreign_keys = ON;
                """;
            normalize.ExecuteNonQuery();
        }

        if (!userTableExisted)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = "admin",
                PasswordHash = "",
                TimeStamp = DateTime.UtcNow
            };
            user.PasswordHash = _passwordService.HashPassword(user, "admin");
            _users.Insert(conn, user);
            _logger.LogInformation("Seeded default admin user (username: admin / password: admin)");
        }
    }
}
