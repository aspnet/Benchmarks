using System;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logBuilder => logBuilder.ClearProviders())
    .ConfigureServices(services =>
    {
        services.AddRateLimiter(options =>
        {
            // Define endpoint limiter
            options.AddConcurrencyLimiter("helloWorld", options =>
            {
                options.PermitLimit = 20000;
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 1000;
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
