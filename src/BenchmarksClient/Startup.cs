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

                        Debug.Assert(process == null);

                        Log($"Running job {jobLogText}");
                        job.State = ClientState.Running;

                        MeasureFirstRequestLatency(job);

                        process = StartProcess(job);
                    }
                    else if (job.State == ClientState.Deleting)
                    {
                        Log($"Deleting job {jobLogText}");

                        Debug.Assert(process != null);

                        try
                        {
                            // We can't wait for ever as the driver is informed that 
                            // the job is pending deletion, and the client would be blocked otherwise
                            if (!process.WaitForExit((job.Duration + 5) * 1000))
                            {
                                process.Kill();
                            }
                        }
                        finally
                        {
                            process.Dispose();
                            process = null;
                        }

                        _jobs.Remove(job.Id);
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

        private static void MeasureFirstRequestLatency(ClientJob job)
        {
            Log("Measuring startup time");

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            using (var response = _httpClient.SendAsync(CreateHttpMessage(job)).GetAwaiter().GetResult())
            {
                var responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                job.LatencyFirstRequest = stopwatch.Elapsed;
            }

            Log("Measuring single connection latency");

            for (var i = 0; i < 10; i++)
            {
                stopwatch.Restart();

                using (var response = _httpClient.SendAsync(CreateHttpMessage(job)).GetAwaiter().GetResult())
                {
                    var responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    // We keep the last measure to simulate a warmup phase.
                    job.LatencyNoLoad = stopwatch.Elapsed;
                }
            }
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
                
                job.State = ClientState.Completed;
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;                
        }

        private static TimeSpan ReadLatency(Match match)
        {
            if (!match.Success || match.Groups.Count != 3)
            {
                throw new NotSupportedException("Failed to parse latency");
            }

            var value = double.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value;

            switch (unit)
            {
                case "s" : return TimeSpan.FromSeconds(value);
                case "ms" : return TimeSpan.FromMilliseconds(value);
                case "us" : return TimeSpan.FromTicks((long)value * 10); // 1 Tick == 100ns == 0.1us

                default: throw new NotSupportedException("Failed to parse latency unit: " + unit);
            }
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}