// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BenchmarkClient;
using Benchmarks.ClientJob;
using Newtonsoft.Json;

namespace BenchmarksClient.Workers
{
    public class WrkWorker : IWorker
    {
        private ClientJob _job;
        private HttpClient _httpClient;
        private Process _process;

        public string JobLogText { get; set; }

        public WrkWorker(ClientJob clientJob)
        {
            _job = clientJob;

            // Configuring the http client to trust the self-signed certificate
            var httpClientHandler = new HttpClientHandler();
            httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClient = new HttpClient(httpClientHandler);

            _job.ClientProperties.TryGetValue("ScriptName", out var scriptName);
            if (_job.ClientProperties.TryGetValue("PipelineDepth", out var pipelineDepth))
            {
                Debug.Assert(int.Parse(pipelineDepth) <= 0 || scriptName != null, "A script name must be present when the pipeline depth is larger than 0.");
            }

            var jobLogText =
                        $"[ID:{_job.Id} Connections:{_job.Connections} Threads:{_job.ClientProperties["Threads"]} Duration:{_job.Duration} Method:{_job.Method} ServerUrl:{_job.ServerBenchmarkUri}";

            if (!string.IsNullOrEmpty(scriptName as string))
            {
                jobLogText += $" Script:{scriptName}";
            }

            if (pipelineDepth != null && int.Parse(pipelineDepth) > 0)
            {
                jobLogText += $" Pipeline:{pipelineDepth}";
            }

            if (_job.Headers != null)
            {
                jobLogText += $" Headers:{JsonConvert.SerializeObject(_job.Headers)}";
            }

            jobLogText += "]";
            JobLogText = jobLogText;
        }

        public async Task StartAsync()
        {
            await MeasureFirstRequestLatency(_job);

            _job.State = ClientState.Running;
            _job.LastDriverCommunicationUtc = DateTime.UtcNow;

            _process = StartProcess(_job);
        }

        public Task StopAsync()
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
            _httpClient.Dispose();
        }

        private async Task MeasureFirstRequestLatency(ClientJob job)
        {
            Startup.Log("Measuring startup time");

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            using (var response = await _httpClient.SendAsync(CreateHttpMessage(job)))
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                job.LatencyFirstRequest = stopwatch.Elapsed;
            }

            Startup.Log(job.LatencyFirstRequest.ToString());

            Startup.Log("Measuring single connection latency");

            for (var i = 0; i < 10; i++)
            {
                stopwatch.Restart();

                using (var response = await _httpClient.SendAsync(CreateHttpMessage(job)))
                {
                    var responseContent = await response.Content.ReadAsStringAsync();

                    // We keep the last measure to simulate a warmup phase.
                    job.LatencyNoLoad = stopwatch.Elapsed;
                }
            }

            Startup.Log(job.LatencyNoLoad.ToString());
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

        private static Process StartProcess(ClientJob job)
        {
            var tcs = new TaskCompletionSource<bool>();

            var command = "wrk";

            if (job.Headers != null)
            {
                foreach (var header in job.Headers)
                {
                    command += $" -H \"{header.Key}: {header.Value}\"";
                }
            }

            command += $" --latency -d {job.Duration} -c {job.Connections} --timeout 8 -t {job.ClientProperties["Threads"]}  {job.ServerBenchmarkUri}{job.Query}";

            if (job.ClientProperties.TryGetValue("ScriptName", out var scriptName))
            {
                if (!string.IsNullOrEmpty(scriptName))
                {
                    command += $" -s scripts/{scriptName}.lua --";

                    var pipeLineDepth = int.Parse(job.ClientProperties["PipelineDepth"]);
                    if (pipeLineDepth > 0)
                    {
                        command += $" {pipeLineDepth}";
                    }

                    if (job.Method != "GET")
                    {
                        command += $" {job.Method}";
                    }
                }
            }

            Startup.Log(command);

            var process = new Process()
            {
                StartInfo = {
                    FileName = "stdbuf",
                    Arguments = $"-oL {command}",
                    WorkingDirectory = Path.GetDirectoryName(typeof(Startup).GetTypeInfo().Assembly.Location),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) =>
            {
                Startup.Log(e.Data);
                job.Output += (e.Data + Environment.NewLine);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                Startup.Log(e.Data);
                job.Error += (e.Data + Environment.NewLine);
            };

            process.Exited += (_, __) =>
            {
                var rpsMatch = Regex.Match(job.Output, @"Requests/sec:\s*([\d\.]*)");
                if (rpsMatch.Success && rpsMatch.Groups.Count == 2)
                {
                    job.RequestsPerSecond = double.Parse(rpsMatch.Groups[1].Value);
                }

                var latencyMatch = Regex.Match(job.Output, @"Latency\s*([\d\.]*)\s*(s|ms|us)");
                job.Latency.Average = ReadLatency(latencyMatch);

                var p50Match = Regex.Match(job.Output, @"50%\s*([\d\.]*)\s*(s|ms|us)");
                job.Latency.Within50thPercentile = ReadLatency(p50Match);

                var p75Match = Regex.Match(job.Output, @"75%\s*([\d\.]*)\s*(s|ms|us)");
                job.Latency.Within75thPercentile = ReadLatency(p75Match);

                var p90Match = Regex.Match(job.Output, @"90%\s*([\d\.]*)\s*(s|ms|us)");
                job.Latency.Within90thPercentile = ReadLatency(p90Match);

                var p99Match = Regex.Match(job.Output, @"99%\s*([\d\.]*)\s*(s|ms|us)");
                job.Latency.Within99thPercentile = ReadLatency(p99Match);

                var socketErrorsMatch = Regex.Match(job.Output, @"Socket errors: connect ([\d\.]*), read ([\d\.]*), write ([\d\.]*), timeout ([\d\.]*)");
                job.SocketErrors = CountSocketErrors(socketErrorsMatch);

                var badResponsesMatch = Regex.Match(job.Output, @"Non-2xx or 3xx responses: ([\d\.]*)");
                job.BadResponses = ReadBadReponses(badResponsesMatch);

                var requestsCountMatch = Regex.Match(job.Output, @"([\d\.]*) requests in ([\d\.]*)(s|m|h)");
                job.Requests = ReadRequests(requestsCountMatch);
                job.ActualDuration = ReadDuration(requestsCountMatch);

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
                throw new NotSupportedException("Failed to parse duration");
            }

            var value = double.Parse(responseCountMatch.Groups[2].Value);

            var unit = responseCountMatch.Groups[3].Value;

            switch (unit)
            {
                case "s": return TimeSpan.FromSeconds(value);
                case "m": return TimeSpan.FromMinutes(value);
                case "h": return TimeSpan.FromHours(value);

                default: throw new NotSupportedException("Failed to parse duration unit: " + unit);
            }
        }

        private static int ReadRequests(Match responseCountMatch)
        {
            if (!responseCountMatch.Success || responseCountMatch.Groups.Count != 3)
            {
                throw new NotSupportedException("Failed to parse requests");
            }

            var value = int.Parse(responseCountMatch.Groups[1].Value);

            return value;
        }

        private static int ReadBadReponses(Match badResponsesMatch)
        {
            if (!badResponsesMatch.Success || badResponsesMatch.Groups.Count != 2)
            {
                return 0;
            }

            var value = int.Parse(badResponsesMatch.Groups[1].Value);

            return value;
        }

        private static double ReadLatency(Match match)
        {
            if (!match.Success || match.Groups.Count != 3)
            {
                throw new NotSupportedException("Failed to parse latency");
            }

            var value = double.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value;

            switch (unit)
            {
                case "s": return value * 1000;
                case "ms": return value;
                case "us": return value / 1000;

                default: throw new NotSupportedException("Failed to parse latency unit: " + unit);
            }
        }

        private static int CountSocketErrors(Match socketErrorsMatch)
        {
            if (!socketErrorsMatch.Success || socketErrorsMatch.Groups.Count != 5)
            {
                return 0;
            }

            var value =
                int.Parse(socketErrorsMatch.Groups[1].Value) +
                int.Parse(socketErrorsMatch.Groups[2].Value) +
                int.Parse(socketErrorsMatch.Groups[3].Value) +
                int.Parse(socketErrorsMatch.Groups[4].Value)
                ;

            return value;
        }
    }
}
