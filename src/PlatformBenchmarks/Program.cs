using System.Net;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;

namespace PlatformBenchmarks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BuildWebHost(args).Run();
        }

        public static IWebHost BuildWebHost(string[] args) =>
            new WebHostBuilder()
                .UseKestrel(options =>
                {
                    options.Listen(IPAddress.IPv6Any, 8080, builder =>
                    {
                        builder.UseHttpApplication<BenchmarkApplication>();
                    });
                })
                .UseStartup<Startup>()
                .Build();
    }
}
