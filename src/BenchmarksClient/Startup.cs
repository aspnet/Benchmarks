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
            var allJobs = _jobs.GetAll();
            // Dequeue the first job. We will only pass jobs that have
            // the same SpanId to the current worker.
            var job = allJobs.FirstOrDefault();
            while (!cancellationToken.IsCancellationRequested)
            {
                if (job != null)
                {
                    if (job.State == ClientJobState.Waiting)
                    {
                        Log($"Starting '{job.Client}' worker");
                        job.State = ClientJobState.Starting;

                        try
                        {
                            if (worker == null)
                            {
                                worker = WorkerFactory.CreateWorker(job);
                            }

                            if (worker == null)
                            {
                                Log($"Error while creating the worker");
                                job.State = ClientJobState.Deleting;
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

                            job.State = ClientJobState.Deleting;
                        }
                    }
                    else if (job.State == ClientJobState.Running || job.State == ClientJobState.Completed)
                    {
                        var now = DateTime.UtcNow;

                        // Clean the job in case the driver is not running
                        if (now - job.LastDriverCommunicationUtc > TimeSpan.FromSeconds(30))
                        {
                            Log($"Driver didn't communicate for {now - job.LastDriverCommunicationUtc}. Halting job.");
                            job.State = ClientJobState.Deleting;
                        }
                    }
                    else if (job.State == ClientJobState.Deleting)
                    {
                        Log($"Deleting job {worker?.JobLogText ?? "no worker found"}");

                        try
                        {
                            if (worker != null)
                            {
                                await worker.StopJobAsync();
                            }
                        }
                        finally
                        {
                            _jobs.Remove(job.Id);
                        }
                    }
                }
                await Task.Delay(100);

                allJobs = _jobs.GetAll();
                if (job != null)
                {
                    job = allJobs.FirstOrDefault(clientJob =>
                    {
                        return string.Equals(clientJob.SpanId, job.SpanId);
                    });
                }
                // job will be null if there aren't any more jobs with the same spanId.
                if (job == null)
                {
                    // Get another job for the new worker we are going to create
                    job = allJobs.FirstOrDefault();

                    // No more jobs with the same span id exist so we can clear
                    // out the worker to signal to the worker factory to create
                    // a new one.
                    if (worker != null)
                    {
                        await worker.DisposeAsync();
                        worker = null;
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
