// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Runtime;
using System.Threading;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Adapter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Benchmarks
{
    public class Program
    {
        public static string[] Args;
        public static string Server;

        public static void Main(string[] args)
        {
            Args = args;

            Console.WriteLine();
            Console.WriteLine("ASP.NET Core Benchmarks");
            Console.WriteLine("-----------------------");

            Console.WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");

            var config = new ConfigurationBuilder()
                .AddCommandLine(args)
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .AddJsonFile("hosting.json", optional: true)
                .Build();

            Server = config["server"] ?? "Kestrel";

            var webHostBuilder = new WebHostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseConfiguration(config)
                .UseStartup<Startup>()
                .ConfigureServices(services => services
                    .AddSingleton(new ConsoleArgs(args))
                    .AddSingleton<IScenariosConfiguration, ConsoleHostScenariosConfiguration>()
                    .AddSingleton<Scenarios>()
                );

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

                    var threads = GetThreadCount(config);

                    if (threads > 0)
                    {
                        options.ThreadCount = threads;
                    }
                });

                webHostBuilder.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
            }
            else if (String.Equals(Server, "WebListener", StringComparison.OrdinalIgnoreCase))
            {
                webHostBuilder = webHostBuilder.UseWebListener();
            }
            else
            {
                throw new InvalidOperationException($"Unknown server value: {Server}");
            }

            var webHost = webHostBuilder.Build();

            Console.WriteLine($"Using server {Server}");
            Console.WriteLine($"Server GC is currently {(GCSettings.IsServerGC ? "ENABLED" : "DISABLED")}");

            var nonInteractiveValue = config["NonInteractive"];
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
            });

            interactiveThread.IsBackground = true;
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
            var uri = new Uri(url);
            var endpoint =  CreateIPEndPoint(uri);
            
            options.Listen(endpoint, listenOptions =>
            {
                var connectionFilter = GetConnectionFilter(config);
                if (connectionFilter != null)
                {
                    listenOptions.ConnectionAdapters.Add(connectionFilter);
                }

                if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                {
                    listenOptions.UseHttps("testCert.pfx", "testPassword");
                }
            });
        }

        private static IPEndPoint CreateIPEndPoint(Uri uri)
        {
            IPAddress ip;

            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                ip = IPAddress.Loopback;
            }
            else if (!IPAddress.TryParse(uri.Host, out ip))
            {
                ip = IPAddress.IPv6Any;
            }

            return new IPEndPoint(ip, uri.Port);
        }
    }
}
