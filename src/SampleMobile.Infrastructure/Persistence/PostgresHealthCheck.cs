using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace SampleMobile.Infrastructure.Persistence;

/// <summary>Readiness check: the shared data source can open a connection and run a trivial query.</summary>
internal sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck {
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        try {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        } catch (Exception exception) when (exception is NpgsqlException or TimeoutException) {
            return new HealthCheckResult(context.Registration.FailureStatus, "PostgreSQL is unreachable.", exception);
        }
    }
}
