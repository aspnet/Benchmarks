using Microsoft.Extensions.Options;
using Npgsql;
using TodosApi;

namespace Microsoft.Extensions.Hosting;

internal static class DatabaseConfiguration
{
    public static IServiceCollection AddDatabase(this IServiceCollection services)
    {
        services.AddSingleton(static sp =>
        {
            var appSettings = sp.GetRequiredService<IOptions<AppSettings>>().Value;
            var db = new NpgsqlSlimDataSourceBuilder(appSettings.ConnectionString).Build();

            return db;
        });
        services.AddHostedService<DatabaseInitializer>();
        return services;
    }
}
