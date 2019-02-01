// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Newtonsoft.Json;

namespace BenchmarksClient.Workers
{
    public class H2LoadWorker : IWorker
    {
        private static HttpClient _httpClient;
        private static HttpClientHandler _httpClientHandler;

        private ClientJob _job;
        private Process _process;

        public string JobLogText { get; set; }

        static H2LoadWorker()
        {
            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClientHandler.MaxConnectionsPerServer = 1;
            _httpClient = new HttpClient(_httpClientHandler);
        }

        private void InitializeJob()
        {
            var jobLogText =
                        $"[ID:{_job.Id} Connections:{_job.Connections} Threads:{_job.Threads} Duration:{_job.Duration} Method:{_job.Method} ServerUrl:{_job.ServerBenchmarkUri}";

            if (_job.Headers != null)
            {
                jobLogText += $" Headers:{JsonConvert.SerializeObject(_job.Headers)}";
            }

            jobLogText += "]";

            JobLogText = jobLogText;
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
            // Ignoring startup latency when using h2c as HttpClient doesn't support

            if (job.ServerBenchmarkUri.StartsWith("http:", StringComparison.OrdinalIgnoreCase))
            {
                Log("Ignoring first request latencies on h2c");
                return;
            }

            if (job.SkipStartupLatencies)
            {
                return;
            }

            Log($"Measuring first request latency on {job.ServerBenchmarkUri}");

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            using (var response = await _httpClient.SendAsync(CreateHttpMessage(job)))
            {
                job.LatencyFirstRequest = stopwatch.Elapsed;
            }

            Log($"{job.LatencyFirstRequest.TotalMilliseconds} ms");

            Log("Measuring subsequent requests latency");

            for (var i = 0; i < 10; i++)
            {
                stopwatch.Restart();

                using (var response = await _httpClient.SendAsync(CreateHttpMessage(job)))
                {
                    // We keep the last measure to simulate a warmup phase.
                    job.LatencyNoLoad = stopwatch.Elapsed;
                }
            }

            Log($"{job.LatencyNoLoad.TotalMilliseconds} ms");
        }

        private static Process StartProcess(ClientJob job)
        {
            var command = $"h2load {job.ServerBenchmarkUri}{job.Query}";

            if (job.Headers != null)
            {
                foreach (var header in job.Headers)
                {
                    command += $" -H \"{header.Key}: {header.Value}\"";
                }
            }

            command += $" -c {job.Connections} -T {job.Timeout} -t {job.Threads}";

            if (job.ClientProperties.TryGetValue("Streams", out var m))
            {
                command += $" -m {m}";
            }

            if (job.ClientProperties.TryGetValue("protocol", out var protocol))
            {
                switch (protocol)
                {
                    case "http": command += " --h1"; break;
                    case "https": command += " --h1"; break;
                    case "h2": break;
                    case "h2c": command += " --no-tls-proto=h2c"; break;
                }
            }

            if (job.ClientProperties.TryGetValue("n", out var n) && int.TryParse(n, out var number))
            {
                command += $" -n{n}";
            }
            else
            {
                command += $" -D {job.Duration}";
            }

            Log(command);

            var process = new Process()
            {
                StartInfo = {
                    FileName = "stdbuf",
                    Arguments = $"-oL {command}",
                    WorkingDirectory = Path.GetDirectoryName(typeof(H2LoadWorker).GetTypeInfo().Assembly.Location),
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

                var rpsMatch = Regex.Match(job.Output, @"([\d\.]+) req/s");
                if (rpsMatch.Success && rpsMatch.Groups.Count == 2)
                {
                    job.RequestsPerSecond = double.Parse(rpsMatch.Groups[1].Value);
                }

                var latencyMatch = Regex.Match(job.Output, @"time to 1st byte: \s+[\d\.]+\w+\s+[\d\.]+\w+\s+([\d\.]+)(\w+)");
                job.Latency.Average = ReadLatency(latencyMatch);

                job.Latency.Within50thPercentile = -1;

                job.Latency.Within75thPercentile = -1;

                job.Latency.Within90thPercentile = -1;

                job.Latency.Within99thPercentile = -1;

                var p100Match = Regex.Match(job.Output, @"time to 1st byte: \s+[\d\.]+\w+\s+([\d\.]+)(\w+)");
                job.Latency.MaxLatency = ReadLatency(p100Match);

                var socketErrorsMatch = Regex.Match(job.Output, @"([\d\.]+) failed, ([\d\.]+) errored, ([\d\.]+) timeout");
                job.SocketErrors = CountSocketErrors(socketErrorsMatch);

                var badResponsesMatch = Regex.Match(job.Output, @"status codes: ([\d\.]+) 2xx, ([\d\.]+) 3xx, ([\d\.]+) 4xx, ([\d\.]+) 5xx");
                job.BadResponses = ReadBadReponses(badResponsesMatch);

                var requestsCountMatch = Regex.Match(job.Output, @"requests: ([\d\.]+) total");
                job.Requests = ReadRequests(requestsCountMatch);

                var durationMatch = Regex.Match(job.Output, @"finished in ([\d\.]+)(\w+)");
                job.ActualDuration = ReadDuration(durationMatch);

                job.State = ClientState.Completed;
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        private static TimeSpan ReadDuration(Match responseCountMatch)
        {
            if (!responseCountMatch.Success || responseCountMatch.Groups.Count != 3)
            {
                Log("Failed to parse duration");
                return TimeSpan.Zero;
            }

            try
            {
                var value = double.Parse(responseCountMatch.Groups[1].Value);

                var unit = responseCountMatch.Groups[2].Value;

                switch (unit)
                {
                    case "s": return TimeSpan.FromSeconds(value);
                    case "m": return TimeSpan.FromMinutes(value);
                    case "h": return TimeSpan.FromHours(value);

                    default: throw new NotSupportedException("Failed to parse duration unit: " + unit);
                }
            }
            catch
            {
                Log("Failed to parse durations");
                return TimeSpan.Zero;
            }
        }

        private static int ReadRequests(Match responseCountMatch)
        {
            if (!responseCountMatch.Success || responseCountMatch.Groups.Count != 2)
            {
                Log("Failed to parse requests");
                return -1;
            }

            try
            {
                return int.Parse(responseCountMatch.Groups[1].Value);
            }
            catch
            {
                Log("Failed to parse requests");
                return -1;
            }
        }

        private static int ReadBadReponses(Match badResponsesMatch)
        {
            if (!badResponsesMatch.Success)
            {
                // wrk does not display the expected line when no bad responses occur
                return 0;
            }

            if (!badResponsesMatch.Success || badResponsesMatch.Groups.Count != 5)
            {
                Log("Failed to parse bad responses");
                return 0;
            }

            try
            {
                return 
                    int.Parse(badResponsesMatch.Groups[3].Value) +
                    int.Parse(badResponsesMatch.Groups[4].Value)
                    ;
            }
            catch
            {
                Log("Failed to parse bad responses");
                return 0;
            }
        }

        private static int CountSocketErrors(Match socketErrorsMatch)
        {
            if (!socketErrorsMatch.Success)
            {
                // wrk does not display the expected line when no errors occur
                return 0;
            }

            if (socketErrorsMatch.Groups.Count != 4)
            {
                Log("Failed to parse socket errors");
                return 0;
            }

            try
            {
                return
                    int.Parse(socketErrorsMatch.Groups[1].Value) +
                    int.Parse(socketErrorsMatch.Groups[2].Value) +
                    int.Parse(socketErrorsMatch.Groups[3].Value) 
                    ;

            }
            catch
            {
                Log("Failed to parse socket errors");
                return 0;
            }

        }

        private static double ReadLatency(Match match)
        {
            if (!match.Success || match.Groups.Count != 3)
            {
                Log("Failed to parse latency");
                return -1;
            }

            try
            {
                var value = double.Parse(match.Groups[1].Value);
                var unit = match.Groups[2].Value;

                switch (unit)
                {
                    case "s": return value * 1000;
                    case "ms": return value;
                    case "us": return value / 1000;

                    default:
                        Log("Failed to parse latency unit: " + unit);
                        return -1;
                }
            }
            catch
            {
                Log("Failed to parse latency");
                return -1;
            }
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