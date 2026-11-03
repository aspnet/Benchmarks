using System.Net;
using System.Runtime.InteropServices;
using ApexFortunes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using PlatformBenchmarks;

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();
var url = new Uri(configuration["urls"] ?? "http://0.0.0.0:5000");

await using var database = await FortuneDatabase.CreateAsync(configuration);
BenchmarkApplication.Database = database;
DateHeader.SyncDateTimer();

var hostBuilder = new WebHostBuilder()
    .UseConfiguration(configuration)
    .UseKestrel(options =>
    {
        options.Listen(IPAddress.Any, url.Port, listen =>
        {
            listen.UseHttpApplication<BenchmarkApplication>();
        });
    })
    .Configure(_ => { });

hostBuilder.UseSockets(options =>
{
    options.WaitForDataBeforeAllocatingBuffer = false;
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        options.UnsafePreferInlineScheduling =
            Environment.GetEnvironmentVariable(
                "DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS") == "1";
    }
});

using var host = hostBuilder.Build();
await host.StartAsync();
Console.WriteLine("Application started.");
await host.WaitForShutdownAsync();
