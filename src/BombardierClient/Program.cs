﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks;
using Newtonsoft.Json.Linq;

namespace BombardierClient
{
    class Program
    {
        private static readonly HttpClient _httpClient;
        private static readonly HttpClientHandler _httpClientHandler;

        private static Dictionary<PlatformID, string> _bombardierUrls = new Dictionary<PlatformID, string>()
        {
            { PlatformID.Win32NT, "https://github.com/codesenberg/bombardier/releases/download/v1.2.4/bombardier-windows-amd64.exe" },
            { PlatformID.Unix, "https://github.com/codesenberg/bombardier/releases/download/v1.2.4/bombardier-linux-amd64" },
        };

        static Program()
        {
            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClientHandler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;

            _httpClient = new HttpClient(_httpClientHandler);
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine("Bombardier Client");
            Console.WriteLine("args: " + String.Join(' ', args));

            Console.Write("Measuring first request ... ");
            await MeasureFirstRequest(args);

            // Extracting parameters
            var argsList = args.ToList();

            TryGetArgumentValue("-w", argsList, out int warmup);
            TryGetArgumentValue("-d", argsList, out int duration);
            TryGetArgumentValue("-n", argsList, out int requests);

            if (duration == 0 && requests == 0)
            {
                Console.WriteLine("Couldn't find valid -d and -n arguments (integers)");
                return;
            }

            TryGetArgumentValue("-w", argsList, out warmup);

            args = argsList.ToArray();

            var bombardierUrl = _bombardierUrls[Environment.OSVersion.Platform];
            var bombardierFileName = Path.GetFileName(bombardierUrl);

            using (var downloadStream = await _httpClient.GetStreamAsync(bombardierUrl))
            using (var fileStream = File.Create(bombardierFileName))
            {
                await downloadStream.CopyToAsync(fileStream);
                if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    Process.Start("chmod", "+x " + bombardierFileName);
                }
            }

            var baseArguments = String.Join(' ', args.ToArray()) + " --print r --format json";

            var process = new Process()
            {
                StartInfo = {
                    FileName = bombardierFileName,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
                EnableRaisingEvents = true
            };

            var stringBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e != null && e.Data != null)
                {
                    Console.WriteLine(e.Data);

                    lock (stringBuilder)
                    {
                        stringBuilder.AppendLine(e.Data);
                    }
                }
            };

            // Warmup

            if (warmup > 0)
            {
                process.StartInfo.Arguments = $" -d {warmup}s {baseArguments}";

                Console.WriteLine("> bombardier " + process.StartInfo.Arguments);

                process.Start();
                process.WaitForExit();
            }

            lock (stringBuilder)
            {
                stringBuilder.Clear();
            }

            process.StartInfo.Arguments = 
                requests > 0
                    ? $" -n {requests} {baseArguments}"
                    : $" -d {duration}s {baseArguments}";

            Console.WriteLine("> bombardier " + process.StartInfo.Arguments);

            process.Start();

            BenchmarksEventSource.SetChildProcessId(process.Id);

            process.BeginOutputReadLine();
            process.WaitForExit();

            string output;

            lock (stringBuilder)
            {
                output = stringBuilder.ToString();
            }

            var document = JObject.Parse(output);

            BenchmarksEventSource.Log.Metadata("bombardier/requests", "max", "sum", "Requests", "Total number of requests", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/badresponses", "max", "sum", "Bad responses", "Non-2xx or 3xx responses", "n0");

            BenchmarksEventSource.Log.Metadata("bombardier/latency/mean", "max", "sum", "Mean latency (us)", "Mean latency (us)", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/latency/max", "max", "sum", "Max latency (us)", "Max latency (us)", "n0");

            BenchmarksEventSource.Log.Metadata("bombardier/rps/mean", "max", "sum", "Requests/sec", "Requests per second", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/rps/max", "max", "sum", "Requests/sec (max)", "Max requests per second", "n0");

            BenchmarksEventSource.Log.Metadata("bombardier/raw", "all", "all", "Raw results", "Raw results", "json");

            var total = 
                document["result"]["req1xx"].Value<int>()
                + document["result"]["req2xx"].Value<int>()
                + document["result"]["req3xx"].Value<int>()
                + document["result"]["req3xx"].Value<int>()
                + document["result"]["req4xx"].Value<int>()
                + document["result"]["req5xx"].Value<int>()
                + document["result"]["others"].Value<int>();

            var success = document["result"]["req2xx"].Value<int>() + document["result"]["req3xx"].Value<int>();

            BenchmarksEventSource.Measure("bombardier/requests", total);
            BenchmarksEventSource.Measure("bombardier/badresponses", total - success);

            BenchmarksEventSource.Measure("bombardier/latency/mean", document["result"]["latency"]["mean"].Value<double>());
            BenchmarksEventSource.Measure("bombardier/latency/max", document["result"]["latency"]["max"].Value<double>());

            BenchmarksEventSource.Measure("bombardier/rps/max", document["result"]["rps"]["max"].Value<double>());
            BenchmarksEventSource.Measure("bombardier/rps/mean", document["result"]["rps"]["mean"].Value<double>());

            BenchmarksEventSource.Measure("bombardier/raw", output);
        }

        private static bool TryGetArgumentValue(string argName, List<string> argsList, out int value)
        {
            var argumentIndex = argsList.FindIndex(arg => string.Equals(arg, argName, StringComparison.OrdinalIgnoreCase));
            if (argumentIndex >= 0)
            {
                string copy = argsList[argumentIndex + 1];
                argsList.RemoveAt(argumentIndex);
                argsList.RemoveAt(argumentIndex);

                return int.TryParse(copy, out value) && value > 0;
            }
            else
            {
                value = default;

                return false;
            }
        }

        public static async Task MeasureFirstRequest(string[] args)
        {
            var url = args.FirstOrDefault(arg => arg.StartsWith("http", StringComparison.OrdinalIgnoreCase));

            if (url == null)
            {
                Console.WriteLine("URL not found, skipping first request");
                return;
            }

            // Configuring the http client to trust the self-signed certificate
            var httpClientHandler = new HttpClientHandler();
            httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            httpClientHandler.MaxConnectionsPerServer = 1;
            using(var httpClient = new HttpClient(httpClientHandler))
            {
                var cts = new CancellationTokenSource(5000);
                var httpMessage = new HttpRequestMessage(HttpMethod.Get, url);

                var stopwatch = new Stopwatch();
                stopwatch.Start();

                try
                {
                    using (var response = await httpClient.SendAsync(httpMessage, cts.Token))
                    {
                        var elapsed = stopwatch.ElapsedMilliseconds;
                        Console.WriteLine($"{elapsed} ms");

                        BenchmarksEventSource.Log.Metadata("http/firstrequest", "max", "avg", "First request (ms)", "First request (ms)", "n0");
                        BenchmarksEventSource.Measure("http/firstrequest", elapsed);
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("A timeout occurred while measuring the first request");
                }
                catch (Exception e)
                {
                    Console.WriteLine("An error occurred while measuring the first request: " + e.ToString());
                }
            }
        }
    }
}
