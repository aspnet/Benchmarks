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
        public static string Key;
        public static string Content;
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
            var cache = Configuration["Cache"] ?? "DistributedMemoryCache";
            var keyLength = int.Parse(Configuration["KeyLength"]);
            var contentSize = int.Parse(Configuration["ContentSize"]);

            CacheOptions.Key = new String('x', keyLength);
            CacheOptions.Content = new String('x', contentSize);
            
            Console.WriteLine($"Key length: {keyLength}");
            Console.WriteLine($"Content size: {contentSize}");

            switch (cache)
            {
                case "StackExchangeRedisCache": 
                    Console.WriteLine("Using StackExchangeRedisCache");
                    services.AddStackExchangeRedisCache(options => 
                    {                         
                        options.Configuration = Configuration["RedisConnectionString"];
                        options.InstanceName = "default";
                    }); 
                    break;

                default: 
                    Console.WriteLine("Using DistributedMemoryCache");
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

                    await context.Response.WriteAsync("OK!");
                });
            });
        }
    }

    public class InitializationService : IHostedService
    {
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
                await cache.SetStringAsync(CacheOptions.Key, CacheOptions.Content, new DistributedCacheEntryOptions { AbsoluteExpiration = DateTimeOffset.MaxValue });
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