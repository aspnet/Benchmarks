// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using Repository;

namespace BenchmarkClient
{
    public class Startup
    {
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

            app.Map("", builder => 
                builder.Run( (context) =>
                {
                    return context.Response.WriteAsync("OK!");
                })
            );
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

            return app.Execute(args);
        }

        private static async Task<int> Run(string url)
        {
            var hostTask = Task.Run(() =>
            {
                var host = new WebHostBuilder()
                    .UseKestrel()
                    .UseStartup<Startup>()
                    .UseUrls(url)
                    .Build();

                host.Run();
            });

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
                        $"[ID:{job.Id} Connections:{job.Connections} Threads:{job.Threads} Duration:{job.Duration} Method:{job.Method}";

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
                        jobLogText += $" Headers:{job.Headers.ToContentString()}";
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

                        process = StartProcess(job);
                    }
                    else if (job.State == ClientState.Deleting)
                    {
                        Log($"Deleting job {jobLogText}");

                        Debug.Assert(process != null);

                        process.WaitForExit();
                        process.Dispose();
                        process = null;

                        _jobs.Remove(job.Id);
                    }
                }
                await Task.Delay(100);
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
                    command += $" -H \"{header}\"";
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
                double rps = -1;
                var rpsMatch = Regex.Match(job.Output, @"Requests/sec:\s*([\d\.]*)");
                if (rpsMatch.Success && rpsMatch.Groups.Count == 2)
                {
                    double.TryParse(rpsMatch.Groups[1].Value, out rps);
                }
                job.RequestsPerSecond = rps;

                double average = -1;
                var latencyMatch = Regex.Match(job.Output, @"Latency\s*([\d\.]*)");
                if (latencyMatch.Success && latencyMatch.Groups.Count == 2)
                {
                    double.TryParse(latencyMatch.Groups[1].Value, out average);
                }
                job.Latency.Average = average;

                // Start Latency Distribution pattent matching after a specific index as 
                // previous results could render a .75% result for instance
                var latencyDistributionIndex = job.Output.IndexOf("Latency Distribution");

                double p50 = -1;
                var p50Match = new Regex(@"50%\s*([\d\.]*)").Match(job.Output, latencyDistributionIndex);
                if (p50Match.Success && p50Match.Groups.Count == 2)
                {
                    double.TryParse(p50Match.Groups[1].Value, out p50);
                }
                job.Latency.P50 = p50;

                double p75 = -1;
                var p75Match = new Regex(@"75%\s*([\d\.]*)").Match(job.Output, latencyDistributionIndex);
                if (p75Match.Success && p75Match.Groups.Count == 2)
                {
                    double.TryParse(p75Match.Groups[1].Value, out p75);
                }
                job.Latency.P75 = p75;

                double p90 = -1;
                var p90Match = new Regex(@"90%\s*([\d\.]*)").Match(job.Output, latencyDistributionIndex);
                if (p90Match.Success && p90Match.Groups.Count == 2)
                {
                    double.TryParse(p90Match.Groups[1].Value, out p90);
                }
                job.Latency.P90 = p90;

                double p99 = -1;
                var p99Match = new Regex(@"99%\s*([\d\.]*)").Match(job.Output, latencyDistributionIndex);
                if (p99Match.Success && p99Match.Groups.Count == 2)
                {
                    double.TryParse(p99Match.Groups[1].Value, out p99);
                }
                job.Latency.P99 = p99;


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
    }
}