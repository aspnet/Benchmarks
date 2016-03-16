// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using BenchmarkServer.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BenchmarkServer
{
    public class Startup
    {
        private const string _url = "http://localhost:5001";
        private const string _benchmarksRepo = @"D:\Git\Benchmarks";

        private static readonly IJobRepository _jobs = new InMemoryJobRepository();

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
            services.AddSingleton(_jobs);
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseMvc();
        }

        public static void Main(string[] args)
        {
            var hostTask = Task.Run(() =>
            {
                var host = new WebHostBuilder()
                    .UseDefaultConfiguration(args)
                    .UseServer("Microsoft.AspNetCore.Server.Kestrel")
                    .UseStartup<Startup>()
                    .UseUrls(_url)
                    .Build();

                host.Run();
            });

            var processJobsCts = new CancellationTokenSource();
            var processJobsTask = ProcessJobs(processJobsCts.Token);

            var completedTask = Task.WhenAny(hostTask, processJobsTask).Result;

            // Propagate exception (and exit process) if either task faulted
            completedTask.Wait();

            // Host exited normally, so cancel job processor
            processJobsCts.Cancel();
            processJobsTask.Wait();
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
                    if (job.State == State.Waiting)
                    {
                        // TODO: Race condition if DELETE is called during this code

                        Log($"Starting job '{job.Id}' with scenario '{job.Scenario}'");
                        job.State = State.Starting;

                        Debug.Assert(process == null);
                        process = StartProcess(job);

                        Log($"Running job '{job.Id}' with scenario '{job.Scenario}'");
                        job.State = State.Running;
                    }
                    else if (job.State == State.Deleting)
                    {
                        Log($"Deleting job '{job.Id}' with scenario '{job.Scenario}'");

                        Debug.Assert(process != null);

                        // Kill process and any child processes started by it
                        Process.Start("taskkill.exe", $"/f /t /pid {process.Id}").WaitForExit();
                        
                        process.Dispose();
                        process = null;

                        _jobs.Remove(job.Id);
                    }
                }
                await Task.Delay(100);
            }
        }

        private static Process StartProcess(Job job)
        {
            var tcs = new TaskCompletionSource<bool>();

            var process = new Process()
            {
                StartInfo = {
                    FileName = "dotnet.exe",
                    Arguments = $"run -- --scenario {job.Scenario}",
                    WorkingDirectory = Path.Combine(_benchmarksRepo, @"src\Benchmarks")
                }
            };

            process.Start();

            return process;
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}