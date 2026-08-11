using Microsoft.Extensions.Diagnostics.HealthChecks;
using PortsideApi.Data;

namespace PortsideApi.Common.HealthChecks;

public sealed class SqliteHealthCheck : IHealthCheck
{
    private readonly SqliteConnectionFactory _dbFactory;

    public SqliteHealthCheck(SqliteConnectionFactory dbFactory) => _dbFactory = dbFactory;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = await _dbFactory.OpenAsync(cancellationToken);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("SQLite reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQLite unreachable", ex);
        }
    }
}
