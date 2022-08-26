using System;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var config = new ConfigurationBuilder()
    .AddJsonFile("hosting.json", optional: true)
    .AddEnvironmentVariables(prefix: "ASPNETCORE_")
    .AddCommandLine(args)
    .Build();

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logBuilder => logBuilder.ClearProviders())
    .ConfigureLogging(loggerFactory =>
    {
        if (Enum.TryParse(config["LogLevel"], out LogLevel logLevel))
        {
            Console.WriteLine($"Console Logging enabled with level '{logLevel}'");
            loggerFactory.AddConsole().SetMinimumLevel(logLevel);
        }
    })
    .ConfigureServices(services =>
    {
        services.AddRateLimiter(options =>
        {
            // Define endpoint limiter
            options.AddSlidingWindowLimiter("helloWorld", options =>
            {
                options.SegmentsPerWindow = 10;
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.PermitLimit = 500;
                options.Window = TimeSpan.FromMilliseconds(1000);
                options.QueueLimit = 100;
            });
        });
    })
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.Configure(app =>
        {
            app.UseRouting();

            app.UseRateLimiter();

            app.UseEndpoints(endpoints =>
            {
                string Plaintext() => "Hello, World!";
                endpoints.MapGet("/plaintext", (Func<string>)Plaintext).RequireRateLimiting("helloWorld");
            });

        });
    })
    .Build();

await host.StartAsync();

Console.WriteLine("Application started.");

await host.WaitForShutdownAsync();
