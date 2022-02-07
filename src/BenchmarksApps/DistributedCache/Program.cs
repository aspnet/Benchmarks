using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;

namespace Benchmarks
{
    public static class CacheOptions
    {
        public readonly static string Key = "abcdefgh";
        public readonly static byte[] Content = new byte[1024];
    }

    public class Startup
    {
        private readonly IConfiguration Configuration;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }
        
        public void ConfigureServices(IServiceCollection services)
        {
            var cache = Configuration["Cache"];

            Console.WriteLine($"Using {cache}");

            switch (cache)
            {
                case "StackExchangeRedisCache": services.AddStackExchangeRedisCache(options => 
                    { 
                        
                        options.Configuration = Configuration["RedisConnectionString"];
                        options.InstanceName = "default";
                    }); 
                    break;

                default: 
                    services.AddDistributedMemoryCache(); 
                    break;
            }

            services.AddHostedService<InitializationService>();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", async context =>
                {
                    var cache = context.RequestServices.GetRequiredService<IDistributedCache>();

                    var cached = await cache.GetStringAsync(CacheOptions.Key);

                    await context.Response.WriteAsync(cached);
                });
            });
        }
    }

    public class InitializationService : IHostedService
    {
        // We need to inject the IServiceProvider so we can create 
        // the scoped service, MyDbContext
        private readonly IServiceProvider _serviceProvider;
        public InitializationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Create a new scope to retrieve scoped services
            using (var scope = _serviceProvider.CreateScope())
            {
                var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
                await cache.SetAsync(CacheOptions.Key, CacheOptions.Content, new DistributedCacheEntryOptions { AbsoluteExpiration = DateTimeOffset.MaxValue });
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                    webBuilder.UseStartup<Startup>()
                );
    }
}