// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Benchmarks.ServerJob;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.PlatformAbstractions;
using Repository;
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
        private const string _defaultUrl = "http://*:5001";
        private static readonly string _defaultHostname = Environment.MachineName.ToLowerInvariant();

        private static readonly IRepository<ServerJob> _jobs = new InMemoryRepository<ServerJob>();

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
                Name = "benchmark-server",
                FullName = "ASP.NET Benchmark Server",
                Description = "REST APIs to run ASP.NET benchmark server"
            };

            app.HelpOption("-?|-h|--help");

            var urlOption = app.Option("-u|--url", $"URL for Rest APIs.  Default value is {_defaultUrl}.",
                CommandOptionType.SingleValue);
            var hostnameOption = app.Option("-n|--hostname", $"Hostname for benchmark server.  Default value is {_defaultHostname}.",
                CommandOptionType.SingleValue);
            var benchmarksRepoOption = app.Option("-b|--benchmarksRepo", "Local path of benchmarks repo.",
                CommandOptionType.SingleValue);

            app.OnExecute(() =>
            {
                var url = urlOption.Value();
                var hostname = hostnameOption.Value();
                var benchmarksRepo = benchmarksRepoOption.Value();

                if (string.IsNullOrWhiteSpace(benchmarksRepo))
                {
                    app.ShowHelp();
                    return 2;
                }
                else
                {
                    url = string.IsNullOrWhiteSpace(url) ? _defaultUrl : url;
                    hostname = string.IsNullOrWhiteSpace(hostname) ? _defaultHostname : hostname;
                    return Run(url, hostname, benchmarksRepo).Result;
                }
            });

            return app.Execute(args);
        }

        private static async Task<int> Run(string url, string hostname, string benchmarksRepo)
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
            var processJobsTask = ProcessJobs(hostname, benchmarksRepo, processJobsCts.Token);

            var completedTask = await Task.WhenAny(hostTask, processJobsTask);

            // Propagate exception (and exit process) if either task faulted
            await completedTask;

            // Host exited normally, so cancel job processor
            processJobsCts.Cancel();
            await processJobsTask;

            return 0;
        }

        private static async Task ProcessJobs(string hostname, string benchmarksRepo, CancellationToken cancellationToken)
        {
            Process process = null;

            GitCommands git = new GitCommands(benchmarksRepo);
            string oldBranch = null;
            string newBranch = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                var allJobs = _jobs.GetAll();
                var job = allJobs.FirstOrDefault();
                if (job != null)
                {
                    if (job.State == ServerState.Waiting)
                    {
                        // TODO: Race condition if DELETE is called during this code

                        Log.WriteLine($"Starting job '{job.Id}' with scenario '{job.Scenario}'");
                        job.State = ServerState.Starting;

                        if (job.PullRequest != 0)
                        {
                            Debug.Assert(oldBranch == null && newBranch == null);

                            oldBranch = git.GetCurrentBranch();                            
                            try
                            {
                                newBranch = git.Fetch(job.PullRequest);
                                git.Checkout(newBranch);
                                git.Merge("dev");
                            }
                            catch
                            {
                                job.State = ServerState.Failed;
                                continue;
                            }
                        }

                        Debug.Assert(process == null);
                        process = StartProcess(hostname, benchmarksRepo, job);
                    }
                    else if (job.State == ServerState.Deleting)
                    {
                        Log.WriteLine($"Deleting job '{job.Id}' with scenario '{job.Scenario}'");

                        Debug.Assert(process != null);

                        // TODO: Replace with managed xplat version of kill process tree
                        Process.Start("taskkill.exe", $"/f /t /pid {process.Id}").WaitForExit();

                        process.Dispose();
                        process = null;

                        if (oldBranch != null)
                        {
                            git.Checkout(oldBranch);
                            oldBranch = null;

                            if (newBranch != null)
                            {
                                git.DeleteBranch(newBranch);
                                newBranch = null;
                            }
                        }

                        _jobs.Remove(job.Id);
                    }
                }
                await Task.Delay(100);
            }
        }

        private static Process StartProcess(string hostname, string benchmarksRepo, ServerJob job)
        {
            var filename = "dotnet";
            var arguments = $"run -c Release -- --scenario {job.Scenario} --server.urls http://{hostname}:5000";

            Log.WriteLine($"Starting process '{filename} {arguments}'");

            var process = new Process()
            {
                StartInfo = {
                    FileName = filename,
                    Arguments = arguments,
                    WorkingDirectory = Path.Combine(benchmarksRepo, @"src\Benchmarks"),
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
                EnableRaisingEvents = true
            };
            process.StartInfo.Environment.Add("COREHOST_SERVER_GC", "1");

            process.OutputDataReceived += (_, e) =>
            {
                if (e != null || e.Data != null)
                {
                    Log.WriteLine(e.Data);

                    if (job.State == ServerState.Starting && e.Data.Contains("Application started"))
                    {
                        job.State = ServerState.Running;
                        job.Url = $"http://{hostname}:5000";
                        Log.WriteLine($"Running job '{job.Id}' with scenario '{job.Scenario}'");
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();

            return process;
        }
    }
}
