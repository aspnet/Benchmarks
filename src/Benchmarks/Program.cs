// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime;
using System.Threading;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Benchmarks
{
    public class Program
    {
        public static string[] Args;
        public static string Server;
        public static string Protocol;

        public static void Main(string[] args)
        {
            Args = args;

            Console.WriteLine();
            Console.WriteLine("ASP.NET Core Benchmarks");
            Console.WriteLine("-----------------------");

            Console.WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");
            Console.WriteLine($"WebHostBuilder loading from: {typeof(WebHostBuilder).GetTypeInfo().Assembly.Location}");

            Console.WriteLine($"NetCore.App location: {typeof(Console).GetTypeInfo().Assembly.Location}");

            var config = new ConfigurationBuilder()
                .AddJsonFile("hosting.json", optional: true)
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .AddCommandLine(args)
                .Build();

            Server = config["server"] ?? "Kestrel";

            Protocol = config["protocol"] ?? "";

            var webHostBuilder = new WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseConfiguration(config)
                .UseStartup<Startup>()
                .ConfigureLogging(loggerFactory =>
                {
                    if (Enum.TryParse(config["LogLevel"], out LogLevel logLevel))
                    {
                        Console.WriteLine($"Console Logging enabled with level '{logLevel}'");
                        loggerFactory.AddConsole().SetMinimumLevel(logLevel);
                    }
                })
                .ConfigureServices(services => services
                    .AddSingleton(new ConsoleArgs(args))
                    .AddSingleton<IScenariosConfiguration, ConsoleHostScenariosConfiguration>()
                    .AddSingleton<Scenarios>()
                )
                .UseDefaultServiceProvider(
                    (context, options) => options.ValidateScopes = context.HostingEnvironment.IsDevelopment());

            bool? threadPoolDispatching = null;
            if (String.Equals(Server, "Kestrel", StringComparison.OrdinalIgnoreCase))
            {
                webHostBuilder = webHostBuilder.UseKestrel(options =>
                {
                    var urls = config["urls"] ?? config["server.urls"];

                    if (!string.IsNullOrEmpty(urls))
                    {
                        foreach (var value in urls.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            Listen(options, config, value);
                        }
                    }
                    else
                    {
                        Listen(options, config, "http://localhost:5000/");
                    }

                    var kestrelThreadPoolDispatchingValue = config["KestrelThreadPoolDispatching"];
                    if (kestrelThreadPoolDispatchingValue != null)
                    {
                        if (bool.Parse(kestrelThreadPoolDispatchingValue))
                        {
                            options.ApplicationSchedulingMode = SchedulingMode.ThreadPool;
                        }
                        else
                        {
                            options.ApplicationSchedulingMode = SchedulingMode.Inline;
                        }
                    }
                });

                var threadCount = GetThreadCount(config);
                var kestrelTransport = config["KestrelTransport"];

                if (threadPoolDispatching == false || string.Equals(kestrelTransport, "Libuv", StringComparison.OrdinalIgnoreCase))
                {
                    webHostBuilder.UseLibuv(options =>
                    {
                        if (threadCount > 0)
                        {
                            options.ThreadCount = threadCount;
                        }
                        else if (threadPoolDispatching == false)
                        {
                            // If thread pool dispatching is explicitly set to false
                            // and the thread count wasn't specified then use 2 * number of logical cores
                            options.ThreadCount = Environment.ProcessorCount * 2;
                        }

                        Console.WriteLine($"Using Libuv with {options.ThreadCount} threads");
                    });
                }
                else if (string.Equals(kestrelTransport, "Sockets", StringComparison.OrdinalIgnoreCase))
                {
#if NETCOREAPP2_1 || NETCOREAPP2_2
                    webHostBuilder.UseSockets(x =>
                    {
                        if (threadCount > 0)
                        {
                            x.IOQueueCount = threadCount;
                        }

                        Console.WriteLine($"Using Sockets with {x.IOQueueCount} threads");
                    });
#else
                    webHostBuilder.UseSockets();
                    Console.WriteLine($"Using Sockets");
#endif
                }
                else if (string.IsNullOrEmpty(kestrelTransport))
                {
                    throw new InvalidOperationException($"Transport must be specified");
                }
                else
                {
                    throw new InvalidOperationException($"Unknown transport {kestrelTransport}");
                }

                webHostBuilder.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
            }
            else if (String.Equals(Server, "HttpSys", StringComparison.OrdinalIgnoreCase))
            {
                webHostBuilder = webHostBuilder.UseHttpSys();
            }
//#if NETCOREAPP2_2 || NETCOREAPP3_0
//            else if (String.Equals(Server, "IISInProcess", StringComparison.OrdinalIgnoreCase))
//            {
//                webHostBuilder = webHostBuilder.UseIIS();
//            }
//#endif
//            else if (String.Equals(Server, "IISOutOfProcess", StringComparison.OrdinalIgnoreCase))
//            {
//                webHostBuilder = webHostBuilder.UseKestrel().UseIISIntegration();
//            }
            else
            {
                throw new InvalidOperationException($"Unknown server value: {Server}");
            }

            var webHost = webHostBuilder.Build();

            Console.WriteLine($"Using server {Server}");
            Console.WriteLine($"Server GC is currently {(GCSettings.IsServerGC ? "ENABLED" : "DISABLED")}");

            var nonInteractiveValue = config["nonInteractive"];
            if (nonInteractiveValue == null || !bool.Parse(nonInteractiveValue))
            {
                StartInteractiveConsoleThread();
            }

            webHost.Run();
        }

        private static void StartInteractiveConsoleThread()
        {
            // Run the interaction on a separate thread as we don't have Console.KeyAvailable on .NET Core so can't
            // do a pre-emptive check before we call Console.ReadKey (which blocks, hard)

            var started = new ManualResetEvent(false);

            var interactiveThread = new Thread(() =>
            {
                Console.WriteLine("Press 'C' to force GC or any other key to display GC stats");
                Console.WriteLine();

                started.Set();

                while (true)
                {
                    var key = Console.ReadKey(intercept: true);

                    if (key.Key == ConsoleKey.C)
                    {
                        Console.WriteLine();
                        Console.Write("Forcing GC...");
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        Console.WriteLine(" done!");
                    }
                    else
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Allocated: {GetAllocatedMemory()}");
                        Console.WriteLine($"Gen 0: {GC.CollectionCount(0)}, Gen 1: {GC.CollectionCount(1)}, Gen 2: {GC.CollectionCount(2)}");
                    }
                }
            })
            {
                IsBackground = true
            };

            interactiveThread.Start();

            started.WaitOne();
        }

        private static string GetAllocatedMemory(bool forceFullCollection = false)
        {
            double bytes = GC.GetTotalMemory(forceFullCollection);

            return $"{((bytes / 1024d) / 1024d).ToString("N2")} MB";
        }

        private static int GetThreadCount(IConfigurationRoot config)
        {
            var threadCountValue = config["threadCount"];
            return threadCountValue == null ? -1 : int.Parse(threadCountValue);
        }

        private static IConnectionAdapter GetConnectionFilter(IConfigurationRoot config)
        {
            var connectionFilterValue = config["connectionFilter"];
            if (string.IsNullOrEmpty(connectionFilterValue))
            {
                return null;
            }
            else
            {
                var connectionFilterType = Type.GetType(connectionFilterValue, throwOnError: true);
                return (IConnectionAdapter)Activator.CreateInstance(connectionFilterType);
            }
        }

        private static void Listen(KestrelServerOptions options, IConfigurationRoot config, string url)
        {
            var urlPrefix = UrlPrefix.Create(url);
            var endpoint = CreateIPEndPoint(urlPrefix);

            options.Listen(endpoint, listenOptions =>
            {
                var connectionFilter = GetConnectionFilter(config);
                if (connectionFilter != null)
                {
                    listenOptions.ConnectionAdapters.Add(connectionFilter);
                }

#if !NETCOREAPP2_0 && !NETCOREAPP2_1
                if (Protocol.Equals("h2", StringComparison.OrdinalIgnoreCase))
                {
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                }
                else if (Protocol.Equals("h2c", StringComparison.OrdinalIgnoreCase))
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                }
#endif

                if (urlPrefix.IsHttps)
                {
                    listenOptions.UseHttps("testCert.pfx", "testPassword");
                }
            });
        }

        private static IPEndPoint CreateIPEndPoint(UrlPrefix urlPrefix)
        {
            IPAddress ip;

            if (string.Equals(urlPrefix.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                ip = IPAddress.Loopback;
            }
            else if (!IPAddress.TryParse(urlPrefix.Host, out ip))
            {
                ip = IPAddress.IPv6Any;
            }

            return new IPEndPoint(ip, urlPrefix.PortValue);
        }
    }
}
