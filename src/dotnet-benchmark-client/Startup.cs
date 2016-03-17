// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using BenchmarkClient.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.PlatformAbstractions;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BenchmarkClient
{
    public class Startup
    {
        private const string _defaultUrl = "http://*:5002";

        private static readonly IJobRepository _jobs = new InMemoryJobRepository();

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

            var urlOption = app.Option("-u|--url", $"URL for Rest APIs.  Default value is {_defaultUrl}.", CommandOptionType.SingleValue);
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
                    .UseDefaultConfiguration()
                    .UseServer("Microsoft.AspNetCore.Server.Kestrel")
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
                    if (job.State == State.Waiting)
                    {
                        // TODO: Race condition if DELETE is called during this code

                        Log($"Starting job '{job.Id}' with command '{job.Filename} {job.Arguments}'");
                        job.State = State.Starting;

                        Debug.Assert(process == null);

                        Log($"Running job '{job.Id}' with command '{job.Filename} {job.Arguments}'");
                        job.State = State.Running;

                        process = StartProcess(benchmarksRepo, job);
                    }
                    else if (job.State == State.Deleting)
                    {
                        Log($"Deleting job '{job.Id}' with command '{job.Filename} {job.Arguments}'");

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

        private static Process StartProcess(string benchmarksRepo, Job job)
        {
            var tcs = new TaskCompletionSource<bool>();

            var process = new Process()
            {
                StartInfo = {
                    FileName = job.Filename,
                    Arguments = job.Arguments,
                    WorkingDirectory = benchmarksRepo,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.EnableRaisingEvents = true;

            var resultBuilder = new StringBuilder();

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
                job.State = State.Completed;
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