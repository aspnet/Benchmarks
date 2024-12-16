// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
#if DATABASE
using Npgsql;
#endif

namespace PlatformBenchmarks
{
    public class Program
    {
        public static string[] Args;

        public static async Task Main(string[] args)
        {
            Args = args;

            Console.WriteLine(Encoding.UTF8.GetString(BenchmarkApplication.ApplicationName));
#if !DATABASE
            Console.WriteLine(Encoding.UTF8.GetString(BenchmarkApplication.Paths.Plaintext));
            Console.WriteLine(Encoding.UTF8.GetString(BenchmarkApplication.Paths.Json));
#else
            Console.WriteLine(Encoding.UTF8.GetString(BenchmarkApplication.Paths.FortunesRaw));
            Console.WriteLine(Encoding.UTF8.GetString(BenchmarkApplication.Paths.FortunesDapper));
            Console.WriteLine(Encoding.UTF8.GetString(BenchmarkApplication.Paths.FortunesEf));
            Console.WriteLine(Encoding.UTF8.GetString(BenchmarkApplication.Paths.SingleQuery));
            Console.WriteLine(Encoding.UTF8.GetString(BenchmarkApplication.Paths.Updates));
            Console.WriteLine(Encoding.UTF8.GetString(BenchmarkApplication.Paths.MultipleQueries));
#endif
            DateHeader.SyncDateTimer();

            var host = BuildWebHost(args);
            var config = (IConfiguration)host.Services.GetService(typeof(IConfiguration));
            BatchUpdateString.DatabaseServer = config.Get<AppSettings>().Database;
#if DATABASE
            try
            {
                await BenchmarkApplication.RawDb.PopulateCache();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error trying to populate database cache: {ex}");
            }
#endif
            await host.RunAsync();
        }

        public static IWebHost BuildWebHost(string[] args)
        {
            Console.WriteLine($"BuildWebHost()");
            Console.WriteLine($"Args: {string.Join(' ', args)}");

            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
#if DEBUG || DEBUG_DATABASE
                .AddUserSecrets<Program>()
#endif
                .AddEnvironmentVariables()
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .AddCommandLine(args)
                .Build();

            var appSettings = config.Get<AppSettings>();
#if DATABASE
            Console.WriteLine($"Database: {appSettings.Database}");
            Console.WriteLine($"ConnectionString: {appSettings.ConnectionString}");

            if (appSettings.Database == DatabaseServer.PostgreSql)
            {
                BenchmarkApplication.RawDb = new RawDb(new ConcurrentRandom(), appSettings);
                BenchmarkApplication.DapperDb = new DapperDb(appSettings);
                BenchmarkApplication.EfDb = new EfDb(appSettings);
            }
            else
            {
                throw new NotSupportedException($"Database '{appSettings.Database}' is not supported, check your app settings.");
            }
#endif

            var hostBuilder = new WebHostBuilder()
                .UseBenchmarksConfiguration(config)
                //.ConfigureLogging(logging => logging.AddConsole())
                .UseKestrel((context, options) =>
                {
                    var endPoints = context.Configuration.CreateIPEndPoints();

                    foreach (var endPoint in endPoints)
                    {
                        options.Listen(endPoint, builder =>
                        {
                            builder.UseHttpApplication<BenchmarkApplication>();
                        });
                    }
                })
                .UseStartup<Startup>();

            hostBuilder.UseSockets(options =>
            {
                options.WaitForDataBeforeAllocatingBuffer = false;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    options.UnsafePreferInlineScheduling = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS") == "1";
                }
            });

            var host = hostBuilder.Build();

            return host;
        }
    }
}
