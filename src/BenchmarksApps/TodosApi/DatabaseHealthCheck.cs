using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace TodosApi;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly NpgsqlDataSource _dataSource;

    public DatabaseHealthCheck(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        object? result = null;
        Exception? exception = null;
        try
        {
            result = await _dataSource.ExecuteScalarAsync("SELECT id FROM public.todos LIMIT 1");
        }
        catch (Exception ex)
        {
            exception = ex;
        }
        
        return result switch
        {
            null or int => HealthCheckResult.Healthy("Database health verified successfully"),
            _ => HealthCheckResult.Unhealthy("Error occurred when checking database health", exception: exception)
        };
    }
}
