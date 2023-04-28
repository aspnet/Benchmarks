#if ENABLE_OPENAPI
using Microsoft.OpenApi.Models;
#endif

namespace Microsoft.Extensions.Hosting;

internal static class OpenApiExtensions
{
    public static IServiceCollection AddOpenApi(this IServiceCollection services)
    {
#if ENABLE_OPENAPI
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Todos API", Version = "v1" });
        });
#endif
        return services;
    }
}
