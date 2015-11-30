// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Threading;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Http.Features;
using Microsoft.AspNet.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Benchmarks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var hostingConfig = new ConfigurationBuilder()
                .AddJsonFile("hosting.json", optional: true)
                .AddCommandLine(args)
                .Build();

            var hostBuilder = new WebHostBuilder(hostingConfig, captureStartupErrors: true);
            hostBuilder.UseStartup(typeof(Startup));

            var host = hostBuilder.Build();

            using (var app = host.Start())
            {
                // Echo out the addresses we're listening on
                var hostingEnv = app.Services.GetRequiredService<IHostingEnvironment>();
                Console.WriteLine("Hosting environment: " + hostingEnv.EnvironmentName);

                var serverAddresses = app.ServerFeatures.Get<IServerAddressesFeature>();
                if (serverAddresses != null)
                {
                    foreach (var address in serverAddresses.Addresses)
                    {
                        Console.WriteLine("Now listening on: " + address);
                    }
                }

                Console.WriteLine("Application started. Press Ctrl+C to shut down.");

                var appLifetime = app.Services.GetRequiredService<IApplicationLifetime>();

                // Run the interaction on a separate thread as we don't have Console.KeyAvailable on .NET Core so can't
                // do a pre-emptive check before we call Console.ReadKey (which blocks, hard)
                var interactiveThread = new Thread(() =>
                {
                    Console.WriteLine();
                    Console.WriteLine("Press 'C' to force GC or any other key to display GC stats");

                    while (true)
                    {
                        var key = Console.ReadKey(intercept: true);

                        if (key.Key == ConsoleKey.C)
                        {
                            Console.WriteLine();
                            Console.Write("Forcing GC...");
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

                // Handle Ctrl+C in order to gracefully shutdown the web server
                Console.CancelKeyPress += (sender, eventArgs) =>
                {
                    Console.WriteLine();
                    Console.WriteLine("Shutting down application...");

                    appLifetime.StopApplication();

                    eventArgs.Cancel = true;
                };

                interactiveThread.Start();

                appLifetime.ApplicationStopping.WaitHandle.WaitOne();
            }
        }

        private static string GetAllocatedMemory(bool forceFullCollection = false)
        {
            double bytes = GC.GetTotalMemory(forceFullCollection);

            return $"{((bytes / 1024d) / 1024d).ToString("N2")} MB";
        }
    }
}
