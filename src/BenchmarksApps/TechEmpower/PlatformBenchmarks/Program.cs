// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Configuration;
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

                    // Allow multiple processes bind to the same port. This also "works" on Windows in that it will
                    // prevent address in use errors and hand off to another process if no others are available,
                    // but it wouldn't round-robin new connections between processes like it will on Linux.
                    options.CreateBoundListenSocket = endpoint =>
                    {
                        if (endpoint is not IPEndPoint ip)
                        {
                            return SocketTransportOptions.CreateDefaultBoundListenSocket(endpoint);
                        }

                        // Normally, we'd call CreateDefaultBoundListenSocket for the IPEndpoint too, but we need
                        // to set ReuseAddress before calling bind, and CreateDefaultBoundListenSocket calls bind.
                        var listenSocket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                        // Kestrel expects IPv6Any to bind to both IPv6 and IPv4
                        if (ip.Address.Equals(IPAddress.IPv6Any))
                        {
                            listenSocket.DualMode = true;
                        }

                        listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        listenSocket.Bind(ip);

                        return listenSocket;
                    };
                }
            });

            var host = hostBuilder.Build();

            return host;
        }
    }
}
