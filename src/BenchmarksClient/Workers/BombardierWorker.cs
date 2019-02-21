// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Newtonsoft.Json.Linq;

namespace BenchmarksClient.Workers
{
    public class BombardierWorker : IWorker
    {
        private static HttpClient _httpClient;
        private static HttpClientHandler _httpClientHandler;

        private ClientJob _job;
        private Process _process;

        public string JobLogText { get; set; }

        static BombardierWorker()
        {
            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClientHandler.MaxConnectionsPerServer = 1;
            _httpClient = new HttpClient(_httpClientHandler);
        }

        private void InitializeJob()
        {
            // TODO: Populate JobText
        }

        public async Task StartJobAsync(ClientJob job)
        {
            _job = job;
            InitializeJob();

            await MeasureFirstRequestLatencyAsync(_job);

            _job.State = ClientState.Running;
            _job.LastDriverCommunicationUtc = DateTime.UtcNow;

            _process = StartProcess(_job);
        }

        public Task StopJobAsync()
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _process.Dispose();
            _process = null;
        }
        private static HttpRequestMessage CreateHttpMessage(ClientJob job)
        {
            var requestMessage = new HttpRequestMessage(new HttpMethod(job.Method), job.ServerBenchmarkUri);

            foreach (var header in job.Headers)
            {
                requestMessage.Headers.Add(header.Key, header.Value);
            }

            return requestMessage;
        }

        private static async Task MeasureFirstRequestLatencyAsync(ClientJob job)
        {
            if (job.SkipStartupLatencies)
            {
                return;
            }

            Log($"Measuring first request latency on {job.ServerBenchmarkUri}");

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            using (var message = CreateHttpMessage(job))
            {
                using (var response = await _httpClient.SendAsync(message))
                {
                    job.LatencyFirstRequest = stopwatch.Elapsed;
                }
            }

            Log($"{job.LatencyFirstRequest.TotalMilliseconds} ms");

            Log("Measuring subsequent requests latency");

            for (var i = 0; i < 10; i++)
            {
                stopwatch.Restart();

                using (var message = CreateHttpMessage(job))
                {
                    using (var response = await _httpClient.SendAsync(message))
                    {
                        // We keep the last measure to simulate a warmup phase.
                        job.LatencyNoLoad = stopwatch.Elapsed;
                    }
                }
            }

            Log($"{job.LatencyNoLoad.TotalMilliseconds} ms");
        }

        private static Process StartProcess(ClientJob job)
        {

            var command = "bombardier";

            if (job.Headers != null)
            {
                foreach (var header in job.Headers)
                {
                    command += $" -H \"{header.Key}: {header.Value}\"";
                }
            }

            command += $" -l -c {job.Connections} -t {job.Timeout} -p result -o json";

            if (job.ClientProperties.TryGetValue("requests", out var n))
            {
                command += $" -n {n}";
            }
            else
            {
                command += $" -d {job.Duration}s";
            }

            if (job.Method != "GET")
            {
                command += $" -m {job.Method}";
            }

            if (job.ClientProperties.TryGetValue("rate", out var r))
            {
                command += $" -r {r}";
            }

            if (job.ClientProperties.TryGetValue("protocol", out var protocol))
            {
                switch (protocol)
                {
                    case "h2": command += " --http2"; break;
                    case "h2c": command += " --http2"; break;
                }
            }

            command += $" {job.ServerBenchmarkUri}{job.Query}";

            Log(command);

            var process = new Process()
            {
                StartInfo = {
                    FileName = "stdbuf",
                    Arguments = $"-oL {command}",
                    WorkingDirectory = Path.GetDirectoryName(typeof(BombardierWorker).GetTypeInfo().Assembly.Location),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    Log(e.Data);
                    job.Output += (e.Data + Environment.NewLine);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    Log(e.Data);
                    job.Error += (e.Data + Environment.NewLine);
                }
            };

            process.Exited += (_, __) =>
            {
                // Wait for all Output messages to be flushed and available in job.Output
                Thread.Sleep(100);

                dynamic output = JObject.Parse(job.Output);
                var result = output.result;

                job.RequestsPerSecond = result.rps.max; // Using max instead of mean will get us more stable results

                job.Latency.Average = ((double)result.latency.mean) / 1000;

                job.Latency.Within50thPercentile = ((double)result.latency.percentiles["50"]) / 1000;

                job.Latency.Within75thPercentile = ((double)result.latency.percentiles["75"]) / 1000;

                job.Latency.Within90thPercentile = ((double)result.latency.percentiles["90"]) / 1000;

                job.Latency.Within99thPercentile = ((double)result.latency.percentiles["99"]) / 1000;

                job.Latency.MaxLatency = ((double)result.latency.max) / 1000;

                job.SocketErrors = 0;

                job.BadResponses = result.req4xx + result.req5xx;

                job.Requests = result.req1xx + result.req2xx + result.req3xx + result.req4xx + result.req5xx;

                job.ActualDuration = TimeSpan.FromSeconds((double)result.timeTakenSeconds);

                job.State = ClientState.Completed;
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }

        public Task DisposeAsync()
        {
            if (_process != null)
            {
                _process.Dispose();
                _process = null;
            }

            return Task.CompletedTask;
        }
    }
}