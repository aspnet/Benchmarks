// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using BenchmarksClient.Workers;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
                Description = "REST APIs to run ASP.NET benchmark client",
                OptionsComparison = StringComparison.OrdinalIgnoreCase
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
            IWorker worker = null;
            ClientJob job = null;
            var whenLastJobCompleted = DateTime.MinValue;
            var waitForMoreJobs = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                var allJobs = _jobs.GetAll();
                // Dequeue the first job. We will only pass jobs that have
                // the same SpanId to the current worker.
                job = allJobs.FirstOrDefault(newJob =>
                {
                    // If the job is null then we don't have a span id to match against. 
                    // Otherwise we want to pick jobs with the same span id.
                    return job == null || string.Equals(newJob.SpanId, job.SpanId, StringComparison.OrdinalIgnoreCase);
                });

                if (job != null)
                {
                    // A spanId means that a span is defined and we might run
                    // multiple jobs.
                    if (!string.IsNullOrEmpty(job.SpanId))
                    {
                        waitForMoreJobs = true;
                    }
                    if (job.State == ClientState.Waiting)
                    {
                        Log($"Starting '{job.Client}' worker");
                        Log($"Current Job SpanId '{job.SpanId}'");
                        job.State = ClientState.Starting;

                        try
                        {
                            if (worker == null)
                            {
                                worker = WorkerFactory.CreateWorker(job);
                            }

                            if (worker == null)
                            {
                                Log($"Error while creating the worker");
                                job.State = ClientState.Deleting;
                                whenLastJobCompleted = DateTime.UtcNow;
                            }
                            else
                            {
                                await worker.StartJobAsync(job);
                            }
                        }
                        catch (Exception e)
                        {
                            Log($"An unexpected error occured while starting the job {job.Id}");
                            Log(e.ToString());

                            job.State = ClientState.Deleting;
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
                        }
                    }
                    else if (job.State == ClientState.Deleting)
                    {
                        Log($"Deleting job {worker?.JobLogText ?? "no worker found"}");

                        try
                        {
                            if (worker != null)
                            {
                                await worker.StopJobAsync();

                                // Reset the last job completed indicator. 
                                whenLastJobCompleted = DateTime.UtcNow;
                            }
                        }
                        finally
                        {
                            _jobs.Remove(job.Id);
                            job = null;
                        }
                    }
                }
                await Task.Delay(100);

                // job will be null if there aren't any more jobs with the same spanId.
                if (job == null)
                {
                    // Currently no jobs with the same span id exist so we check if we can
                    // clear out the worker to signal to the worker factory to create
                    // a new one.
                    if (worker != null)
                    {
                        var now = DateTime.UtcNow;

                        // Disposing the worker conditions
                        // 1. A span isn't defined so there won't be any more jobs for this worker
                        // 2. We check that whenLastJob completed is something other that it's default value 
                        //    and 10 seconds have passed since the last job was completed.
                        if (!waitForMoreJobs || (whenLastJobCompleted != DateTime.MinValue &&  now - whenLastJobCompleted > TimeSpan.FromSeconds(10)))
                        {
                            Log("Job queuing timeout reached. Disposing worker.");
                            waitForMoreJobs = false;
                            await worker.DisposeAsync();
                            worker = null;
                        }
                    }
                }
            }
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}
