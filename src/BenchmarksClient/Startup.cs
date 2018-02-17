// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Repository;

namespace BenchmarkClient
{
    public class Startup
    {
        private static HttpClient _httpClient;
        private static HttpClientHandler _httpClientHandler;

        private const string _defaultUrl = "http://*:5002";

        private static readonly IRepository<ClientJob> _jobs = new InMemoryRepository<ClientJob>();

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();

            services.AddSingleton(_jobs);
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseMvc();

            // Register a default startup page to ensure the application is up
            app.Run((context) =>
            {
                return context.Response.WriteAsync("OK!");
            });
        }

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                Name = "BenchmarksClient",
                FullName = "ASP.NET Benchmark Client",
                Description = "REST APIs to run ASP.NET benchmark client"
            };

            app.HelpOption("-?|-h|--help");

            var urlOption = app.Option("-u|--url", $"URL for Rest APIs.  Default is '{_defaultUrl}'.", CommandOptionType.SingleValue);

            app.OnExecute(() =>
            {
                var url = urlOption.HasValue() ? urlOption.Value() : _defaultUrl;
                return Run(url).Result;
            });

            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClient = new HttpClient(_httpClientHandler);

            return app.Execute(args);
        }

        private static async Task<int> Run(string url)
        {
            var host = new WebHostBuilder()
                    .UseKestrel()
                    .UseStartup<Startup>()
                    .UseUrls(url)
                    .Build();

            var hostTask = host.RunAsync();

            var processJobsCts = new CancellationTokenSource();
            var processJobsTask = ProcessJobs(processJobsCts.Token);

            var completedTask = await Task.WhenAny(hostTask, processJobsTask);

            // Propagate exception (and exit process) if either task faulted
            await completedTask;

            // Host exited normally, so cancel job processor
            processJobsCts.Cancel();
            await processJobsTask;

            return 0;
        }

        private static async Task ProcessJobs(CancellationToken cancellationToken)
        {
            Process process = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                var allJobs = _jobs.GetAll();
                var job = allJobs.FirstOrDefault();
                if (job != null)
                {
                    var jobLogText =
                        $"[ID:{job.Id} Connections:{job.Connections} Threads:{job.Threads} Duration:{job.Duration} Method:{job.Method} ServerUrl:{job.ServerBenchmarkUri}";

                    Debug.Assert(job.PipelineDepth <= 0 || job.ScriptName != null, "A script name must be present when the pipeline depth is larger than 0.");

                    if (!string.IsNullOrEmpty(job.ScriptName))
                    {
                        jobLogText += $" Script:{job.ScriptName}";
                    }

                    if (job.PipelineDepth > 0)
                    {
                        jobLogText += $" Pipeline:{job.PipelineDepth}";
                    }

                    if (job.Headers != null)
                    {
                        jobLogText += $" Headers:{JsonConvert.SerializeObject(job.Headers)}";
                    }

                    jobLogText += "]";

                    if (job.State == ClientState.Waiting)
                    {
                        // TODO: Race condition if DELETE is called during this code

                        Log($"Starting job {jobLogText}");
                        job.State = ClientState.Starting;

                        try
                        {
                            Debug.Assert(process == null);

                            await MeasureFirstRequestLatencyAsync(job);

                            Log($"Running job {jobLogText}");
                            job.State = ClientState.Running;
                            _jobs.Update(job);

                            job.LastDriverCommunicationUtc = DateTime.UtcNow;

                            process = StartProcess(job);
                        }
                        catch(Exception e)
                        {
                            Log($"An unexpected error occured while starting the job {job.Id}");
                            Log(e.Message);

                            job.State = ClientState.Deleting;
                            _jobs.Update(job);
                        }
                    }
                    else if (job.State == ClientState.Running || job.State == ClientState.Completed)
                    {
                        var now = DateTime.UtcNow;

                        // Clean the job in case the driver is not running
                        if (now - job.LastDriverCommunicationUtc > TimeSpan.FromSeconds(30))
                        {
                            Log($"Driver didn't communicate for {now - job.LastDriverCommunicationUtc}. Halting job.");
                            job.State = ClientState.Deleting;
                            _jobs.Update(job);
                        }
                    }
                    else if (job.State == ClientState.Deleting)
                    {
                        Log($"Deleting job {jobLogText}");

                        try
                        {
                            if (process != null && !process.HasExited)
                            {
                                process.Kill();
                            }
                        }
                        finally
                        {
                            process.Dispose();
                            process = null;

                            _jobs.Remove(job.Id);
                        }
                    }
                }
                await Task.Delay(100);
            }
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

            using (var response = await _httpClient.SendAsync(CreateHttpMessage(job)))
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                job.LatencyFirstRequest = stopwatch.Elapsed;
            }

            Log($"{job.LatencyFirstRequest.TotalMilliseconds} ms");

            Log("Measuring subsequent requests latency");

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

            Log($"{job.LatencyNoLoad.TotalMilliseconds} ms");
        }

        private static Process StartProcess(ClientJob job)
        {
            var command = "wrk";

            if (job.Headers != null)
            {
                foreach (var header in job.Headers)
                {
                    command += $" -H \"{header.Key}: {header.Value}\"";
                }
            }

            command += $" --latency -d {job.Duration} -c {job.Connections} --timeout 8 -t {job.Threads}  {job.ServerBenchmarkUri}{job.Query}";

            if (!string.IsNullOrEmpty(job.ScriptName))
            {
                command += $" -s scripts/{job.ScriptName}.lua --";

                if (job.PipelineDepth > 0)
                {
                    command += $" {job.PipelineDepth}";
                }

                if (job.Method != "GET")
                {
                    command += $" {job.Method}";
                }
            }

            Log(command);

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
                Log(e.Data);
                job.Output += (e.Data + Environment.NewLine);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                Log(e.Data);
                job.Error += (e.Data + Environment.NewLine);
            };

            process.Exited += (_, __) =>
            {
                var rpsMatch = Regex.Match(job.Output, @"Requests/sec:\s*([\d\.]*)");
                if (rpsMatch.Success && rpsMatch.Groups.Count == 2)
                {
                    job.RequestsPerSecond = double.Parse(rpsMatch.Groups[1].Value);
                }

                const string LatencyPattern = @"\s*([\d\.]*)(\w*)";

                var latencyMatch = Regex.Match(job.Output, "Latency" + LatencyPattern);
                job.Latency.Average = ReadLatency(latencyMatch);

                var p50Match = Regex.Match(job.Output, "50%" + LatencyPattern);
                job.Latency.Within50thPercentile = ReadLatency(p50Match);

                var p75Match = Regex.Match(job.Output, "75%" + LatencyPattern);
                job.Latency.Within75thPercentile = ReadLatency(p75Match);

                var p90Match = Regex.Match(job.Output, "90%" + LatencyPattern);
                job.Latency.Within90thPercentile = ReadLatency(p90Match);

                var p99Match = Regex.Match(job.Output, "99%" + LatencyPattern);
                job.Latency.Within99thPercentile = ReadLatency(p99Match);

                var socketErrorsMatch = Regex.Match(job.Output, @"Socket errors: connect ([\d\.]*), read ([\d\.]*), write ([\d\.]*), timeout ([\d\.]*)");
                job.SocketErrors = CountSocketErrors(socketErrorsMatch);

                var badResponsesMatch = Regex.Match(job.Output, @"Non-2xx or 3xx responses: ([\d\.]*)");
                job.BadResponses = ReadBadReponses(badResponsesMatch);

                var requestsCountMatch = Regex.Match(job.Output, @"([\d\.]*) requests in ([\d\.]*)(\w*)");
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
            if (!responseCountMatch.Success || responseCountMatch.Groups.Count != 4)
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
            if (!responseCountMatch.Success || responseCountMatch.Groups.Count != 4)
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

        private static double ReadLatency(Match match)
        {
            if (!match.Success || match.Groups.Count != 3)
            {
                Log("Failed to parse latency");
                return -1;
            }

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

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}
