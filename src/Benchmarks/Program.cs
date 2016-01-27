// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Benchmarks
{
    public class Program
    {
        public static string[] Args;
        
        public static void Main(string[] args)
        {
            Args = args;
            
            Console.WriteLine();
            Console.WriteLine("ASP.NET Core Benchmarks");
            Console.WriteLine("-----------------------");

            var scenarios = LoadScenarios(args);

            StartInteractiveConsoleThread();

            var webHost = new WebHostBuilder()
                .UseServer("Microsoft.AspNetCore.Server.Kestrel")
                .UseCaptureStartupErrors(false)
                .UseDefaultConfiguration(args)
                .UseStartup<Startup>()
                .ConfigureServices(services => services.AddSingleton(scenarios))
                .Build();

            webHost.Run();
        }

        private static Scenarios LoadScenarios(string[] args)
        {
            var scenarioConfig = new ConfigurationBuilder()
                .AddJsonFile("scenarios.json", optional: true)
                .AddCommandLine(args)
                .Build()
                .GetChildren()
                .ToList();

            var scenarios = new Scenarios();
            var enabledCount = 0;

            if (scenarioConfig.Count > 0)
            {
                Console.WriteLine("Scenario configuration found in scenarios.json and/or command line args");

                foreach (var scenario in scenarioConfig)
                {
                    enabledCount += scenarios.Enable(scenario.Value);
                }
            }
            else
            {
                Console.WriteLine("Which scenarios would you like to enable?:");
                Console.WriteLine();
                foreach (var scenario in scenarios.GetNames())
                {
                    Console.WriteLine("  " + scenario);
                }
                Console.WriteLine();
                Console.WriteLine("Type full or partial scenario names separated by commas and hit [Enter]");
                Console.Write("> ");

                var choices = Console.ReadLine().Split(new [] {','}, StringSplitOptions.RemoveEmptyEntries);

                if (choices.Length > 0)
                {
                    foreach (var choice in choices)
                    {
                        enabledCount += scenarios.Enable(choice);
                    }
                }
            }

            if (enabledCount == 0)
            {
                Console.WriteLine();
                Console.WriteLine("No matching scenarios found, enabling defaults");
                scenarios.EnableDefault();
            }

            PrintEnabledScenarios(scenarios.GetEnabled());
            
            return scenarios;
        }

        private static void PrintEnabledScenarios(IEnumerable<Scenarios.EnabledScenario> scenarios)
        {
            Console.WriteLine();
            Console.WriteLine("The following scenarios were enabled:");

            var maxNameLength = scenarios.Max(s => s.Name.Length);

            foreach (var scenario in scenarios)
            {
                Console.WriteLine($"  {scenario.Name.PadRight(maxNameLength)} -> {string.Join($"{Environment.NewLine}{"".PadLeft(maxNameLength + 6)}", scenario.Paths)}");
            }
            Console.WriteLine();
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
    }
}
