// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.Extensions.CommandLineUtils;

namespace BenchmarkDriver
{
    public class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                Name = "benchmark-driver",
                FullName = "ASP.NET Benchmark Driver",
                Description = "Driver for ASP.NET Benchmarks"
            };

            app.HelpOption("-?|-h|--help");

            var scenarioOption = app.Option("-n|--scenario", "Benchmark scenario to run", CommandOptionType.SingleValue);
            var serverOption = app.Option("-s|--server", "URL of benchmark server", CommandOptionType.SingleValue);
            var clientOption = app.Option("-c|--client", "URL of benchmark client", CommandOptionType.SingleValue);

            app.OnExecute(() =>
            {
                var scenario = scenarioOption.Value();
                var server = serverOption.Value();
                var client = clientOption.Value();

                if (string.IsNullOrWhiteSpace(scenario) || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(client))
                {
                    app.ShowHelp();
                    return 2;
                }
                else
                {
                    return Run(scenario, new Uri(server), new Uri(client)).Result;
                }
            });

            return app.Execute(args);
        }

        private static async Task<int> Run(string scenario, Uri serverUri, Uri clientUri)
        {
            var serverJobs = new Uri(serverUri, "/jobs");

            var content = $"{{'scenario': '{scenario}'}}";
            Log($"Starting scenario {scenario} on benchmark server...");
            LogVerbose($"POST {serverJobs} {content}...");
            var response = await _httpClient.PostAsync(serverJobs, new StringContent(content, Encoding.UTF8, "application/json"));
            var responseContent = await response.Content.ReadAsStringAsync();
            LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            var serverJob = new Uri(serverUri, response.Headers.Location);

            while (true)
            {
                LogVerbose($"GET {serverJob}...");
                response = await _httpClient.GetAsync(serverJob);
                responseContent = await response.Content.ReadAsStringAsync();

                LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

                // TODO: Replace with JSON.NET
                if (responseContent.Contains("\"State\":2"))
                {
                    break;
                }
                else
                {
                    await Task.Delay(1000);
                }
            }

            Log($"Scenario {scenario} running on benchmark server");

            Log($"Starting scenario {scenario} on benchmark client...");

            // TODO: Replace with call to BenchmarkClient
            var benchmarkUri = new Uri($"http://localhost:5000/{scenario}");
            LogVerbose($"GET {benchmarkUri}...");
            response = await _httpClient.GetAsync(benchmarkUri);
            responseContent = await response.Content.ReadAsStringAsync();
            LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");
            response.EnsureSuccessStatusCode();

            Log($"Results: ");

            Log($"Stopping scenario {scenario} on benchmark server...");

            LogVerbose($"DELETE {serverJob}...");
            response = _httpClient.DeleteAsync(serverJob).Result;
            LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");
            response.EnsureSuccessStatusCode();

            return 0;
        }

        private static void Log(string message)
        {
            Log(message, Reporter.Output);
        }

        private static void LogVerbose(string message)
        {
            Log(message, Reporter.Verbose);
        }

        private static void Log(string message, Reporter reporter)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            reporter.WriteLine($"[{time}] {message}");
        }
    }
}