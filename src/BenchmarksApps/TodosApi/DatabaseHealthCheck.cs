using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nanorm;
using Npgsql;

namespace TodosApi;

internal class DatabaseHealthCheck : IHealthCheck
{
    private readonly NpgsqlDataSource _dataSource;

    public DatabaseHealthCheck(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        Exception? exception = null;
        try
        {
            await _dataSource.ExecuteScalarAsync("SELECT 1", cancellationToken);
        }
        catch (Exception ex)
        {
            exception = ex;
        }
        
        return exception switch
        {
            null => HealthCheckResult.Healthy("Database health verified successfully"),
            _ => HealthCheckResult.Unhealthy("Error occurred when checking database health", exception: exception)
        };
    }
}
