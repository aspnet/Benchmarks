// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using BenchmarksClient;
using BenchmarksClient.Workers;
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
            IWorker worker = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                var allJobs = _jobs.GetAll();
                var job = allJobs.FirstOrDefault();
                if (job != null)
                {
                    if (job.State == ClientState.Waiting)
                    {
                        // TODO: Race condition if DELETE is called during this code

                        Log($"Starting '{job.ClientName}' worker");
                        job.State = ClientState.Starting;

                        try
                        {
                            if (WorkerFactory.TryCreate(job, out worker, out var error) == false)
                            {
                                Console.WriteLine(error);
                                // Worker failed to start
                                job.State = ClientState.Deleting;
                            }
                            else
                            {
                                Debug.Assert(worker != null);
                                Log($"Starting job {worker.JobLogText}");

                                await worker.StartAsync();

                                Log($"Running job {worker.JobLogText}");
                            }

                            job.LastDriverCommunicationUtc = DateTime.UtcNow;
                        }
                        catch (Exception e)
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

                        if (now - job.LastDriverCommunicationUtc > TimeSpan.FromSeconds(30))
                        {
                            Log($"Driver didn't communicate for {now - job.LastDriverCommunicationUtc}. Halting job.");
                            job.State = ClientState.Deleting;
                            _jobs.Update(job);
                        }
                    }
                    else if (job.State == ClientState.Deleting)
                    {
                        Debug.Assert(worker != null);
                        Log($"Deleting job {worker.JobLogText}");

                        try
                        {
                            await worker.StopAsync();
                        }
                        finally
                        {
                            worker.Dispose();
                            worker = null;

                            _jobs.Remove(job.Id);
                        }
                    }
                }
                await Task.Delay(100);
            }
        }

        public static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}
