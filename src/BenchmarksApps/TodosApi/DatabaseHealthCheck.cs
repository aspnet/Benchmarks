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
        var result = await _dataSource.ExecuteScalarAsync("SELECT COUNT(id) FROM public.todos");
        return result switch
        {
            long count when count >= 0 => HealthCheckResult.Healthy(),
            _ => HealthCheckResult.Unhealthy()
        };
    }
}
