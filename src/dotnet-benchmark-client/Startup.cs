// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Benchmarks.ClientJob;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.PlatformAbstractions;
using Repository;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BenchmarkClient
{
    public class Startup
    {
        private const string _defaultUrl = "http://*:5002";

        private static readonly IRepository<ClientJob> _jobs = new InMemoryRepository<ClientJob>();

        private IApplicationEnvironment _env;
        public Startup(IApplicationEnvironment environment)
        {
            _env = environment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();

            services.AddSingleton(_jobs);
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseMvc();
        }

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                Name = "benchmark-client",
                FullName = "ASP.NET Benchmark Client",
                Description = "REST APIs to run ASP.NET benchmark client"
            };

            app.HelpOption("-?|-h|--help");

            var urlOption = app.Option("-u|--url", $"URL for Rest APIs.  Default is '{_defaultUrl}'.", CommandOptionType.SingleValue);
            var benchmarksRepoOption = app.Option("-b|--benchmarksRepo", "Local path of benchmarks repo.", CommandOptionType.SingleValue);

            app.OnExecute(() =>
            {
                var url = urlOption.Value();
                var benchmarksRepo = benchmarksRepoOption.Value();

                if (string.IsNullOrWhiteSpace(benchmarksRepo))
                {
                    app.ShowHelp();
                    return 2;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        url = _defaultUrl;
                    }
                    return Run(url, benchmarksRepo).Result;
                }
            });

            return app.Execute(args);
        }

        private static async Task<int> Run(string url, string benchmarksRepo)
        {
            var hostTask = Task.Run(() =>
            {
                var host = new WebHostBuilder()
                    .UseDefaultHostingConfiguration()
                    .UseKestrel()
                    .UseStartup<Startup>()
                    .UseUrls(url)
                    .Build();

                host.Run();
            });

            var processJobsCts = new CancellationTokenSource();
            var processJobsTask = ProcessJobs(benchmarksRepo, processJobsCts.Token);

            var completedTask = await Task.WhenAny(hostTask, processJobsTask);

            // Propagate exception (and exit process) if either task faulted
            await completedTask;

            // Host exited normally, so cancel job processor
            processJobsCts.Cancel();
            await processJobsTask;

            return 0;
        }

        private static async Task ProcessJobs(string benchmarksRepo, CancellationToken cancellationToken)
        {
            Process process = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                var allJobs = _jobs.GetAll();
                var job = allJobs.FirstOrDefault();
                if (job != null)
                {
                    var jobLogText = $"[ID:{job.Id} Connections:{job.Connections} Threads:{job.Threads} Duration:{job.Duration} Pipeline:{job.PipelineDepth}]";

                    if (job.State == ClientState.Waiting)
                    {
                        // TODO: Race condition if DELETE is called during this code

                        Log($"Starting job {jobLogText}");
                        job.State = ClientState.Starting;

                        Debug.Assert(process == null);

                        Log($"Running job {jobLogText}");
                        job.State = ClientState.Running;

                        process = StartProcess(benchmarksRepo, job);
                    }
                    else if (job.State == ClientState.Deleting)
                    {
                        Log($"Deleting job {jobLogText}'");

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

        private static Process StartProcess(string benchmarksRepo, ClientJob job)
        {
            var tcs = new TaskCompletionSource<bool>();

            var command = $"wrk -c {job.Connections} -t {job.Threads} -d {job.Duration}";
            if (job.PipelineDepth > 0)
            {
                command += $" -s {benchmarksRepo}/scripts/pipeline.lua";
            }
            command += $" {job.ServerBenchmarkUri}";
            if (job.PipelineDepth > 0)
            {
                command += $" -- {job.PipelineDepth}";
            }

            var process = new Process()
            {
                StartInfo = {
                    FileName = "stdbuf",
                    Arguments = $"-oL {command}",
                    WorkingDirectory = benchmarksRepo,
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
                var match = Regex.Match(job.Output, @"Requests/sec:\s*([\d.]*)");
                if (match.Success && match.Groups.Count == 2)
                {
                    double.TryParse(match.Groups[1].Value, out rps);
                }
                job.RequestsPerSecond = rps;

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