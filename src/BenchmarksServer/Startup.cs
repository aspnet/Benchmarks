// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ServerJob;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.PlatformAbstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Repository;

namespace BenchmarkServer
{
    public class Startup
    {
        private const string _benchmarksDir = "Benchmarks";
        private const string _defaultBenchmarksRepoUrl = "https://github.com/aspnet/benchmarks.git";

        private const string _kestrelDir = "KestrelHttpServer";
        private const string _defaultKestrelRepoUrl = "https://github.com/aspnet/KestrelHttpServer.git";

        private const string _defaultBranch = "dev";

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
                Name = "BenchmarksServer",
                FullName = "ASP.NET Benchmark Server",
                Description = "REST APIs to run ASP.NET benchmark server"
            };

            app.HelpOption("-?|-h|--help");

            var urlOption = app.Option("-u|--url", $"URL for Rest APIs.  Default is '{_defaultUrl}'.",
                CommandOptionType.SingleValue);
            var hostnameOption = app.Option("-n|--hostname", $"Hostname for benchmark server.  Default is '{_defaultHostname}'.",
                CommandOptionType.SingleValue);

            app.OnExecute(() =>
            {
                var url = urlOption.HasValue() ? urlOption.Value() : _defaultUrl;
                var hostname = hostnameOption.HasValue() ? hostnameOption.Value() : _defaultHostname;
                return Run(url, hostname).Result;
            });

            return app.Execute(args);
        }

        private static async Task<int> Run(string url, string hostname)
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
            var processJobsTask = ProcessJobs(hostname, processJobsCts.Token);

            var completedTask = await Task.WhenAny(hostTask, processJobsTask);

            // Propagate exception (and exit process) if either task faulted
            await completedTask;

            // Host exited normally, so cancel job processor
            processJobsCts.Cancel();
            await processJobsTask;

            return 0;
        }

        private static async Task ProcessJobs(string hostname, CancellationToken cancellationToken)
        {
            Process process = null;

            string tempDir = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                var allJobs = _jobs.GetAll();
                var job = allJobs.FirstOrDefault();
                if (job != null)
                {
                    if (job.State == ServerState.Waiting)
                    {
                        // TODO: Race condition if DELETE is called during this code
                        try
                        {
                            Log.WriteLine($"Starting job '{job.Id}' with scenario '{job.Scenario}'");
                            job.State = ServerState.Starting;

                            Debug.Assert(tempDir == null);
                            tempDir = GetTempDir();

                            CloneAndRestore(tempDir, job);

                            Debug.Assert(process == null);
                            process = StartProcess(hostname, Path.Combine(tempDir, _benchmarksDir), job);
                        }
                        catch (Exception e)
                        {
                            Log.WriteLine($"Error starting job '{job.Id}': {e}");

                            if (tempDir != null)
                            {
                                DeleteDir(tempDir);
                                tempDir = null;
                            }

                            job.State = ServerState.Failed;
                            continue;
                        }
                    }
                    else if (job.State == ServerState.Deleting)
                    {
                        Log.WriteLine($"Deleting job '{job.Id}' with scenario '{job.Scenario}'");

                        if (process != null)
                        {
                            // TODO: Replace with managed xplat version of kill process tree
                            ProcessUtil.Run("taskkill.exe", $"/f /t /pid {process.Id}", throwOnError: false);
                            process.Dispose();
                            process = null;
                        }

                        if (tempDir != null)
                        {
                            DeleteDir(tempDir);
                            tempDir = null;
                        }

                        _jobs.Remove(job.Id);
                    }
                }
                await Task.Delay(100);
            }
        }

        private static void CloneAndRestore(string path, ServerJob job)
        {
            var benchmarksRepoUrl = string.IsNullOrEmpty(job.BenchmarksRepo) ? _defaultBenchmarksRepoUrl : job.BenchmarksRepo;
            var benchmarksBranch = string.IsNullOrEmpty(job.BenchmarksBranch) ? _defaultBranch : job.BenchmarksBranch;
            Git.Clone(path, benchmarksRepoUrl, benchmarksBranch, _benchmarksDir);

            if (!string.IsNullOrEmpty(job.KestrelBranch))
            {
                var kestrelRepoUrl = string.IsNullOrEmpty(job.KestrelRepo) ? _defaultKestrelRepoUrl : job.KestrelRepo;
                var kestrelBranch = string.IsNullOrEmpty(job.KestrelBranch) ? _defaultBranch : job.KestrelBranch;
                Git.Clone(path, kestrelRepoUrl, kestrelBranch, _kestrelDir);

                ProcessUtil.Run("dotnet", "restore --infer-runtimes", workingDirectory: Path.Combine(path, _kestrelDir));

                // Configure Benchmarks to use Kestrel from sources rather than packages
                var benchmarksGlobalJson = Path.Combine(path, _benchmarksDir, "global.json");
                dynamic globalJson = JsonConvert.DeserializeObject(File.ReadAllText(benchmarksGlobalJson));
                globalJson["projects"].Add(Path.Combine("..", _kestrelDir, "src"));
                File.WriteAllText(benchmarksGlobalJson, JsonConvert.SerializeObject(globalJson, Formatting.Indented));

                // Add references to libuv (required until https://github.com/aspnet/KestrelHttpServer/pull/731)
                var benchmarksProjectJson = Path.Combine(path, _benchmarksDir, "src", "Benchmarks", "project.json");
                dynamic projectJson = JsonConvert.DeserializeObject(File.ReadAllText(benchmarksProjectJson));
                projectJson["dependencies"]["Microsoft.AspNetCore.Internal.libuv-Windows"] = new JObject();
                projectJson["dependencies"]["Microsoft.AspNetCore.Internal.libuv-Windows"]["version"] = "1.0.0-*";
                projectJson["dependencies"]["Microsoft.AspNetCore.Internal.libuv-Windows"]["type"] = "build";
                File.WriteAllText(benchmarksProjectJson, JsonConvert.SerializeObject(projectJson, Formatting.Indented));
            }

            ProcessUtil.Run("dotnet", "restore --infer-runtimes", workingDirectory: Path.Combine(path, _benchmarksDir));
        }

        private static string GetTempDir()
        {
            var temp = Path.GetTempFileName();
            File.Delete(temp);
            Directory.CreateDirectory(temp);

            Log.WriteLine($"Created temp directory '{temp}'");

            return temp;
        }

        private static void DeleteDir(string path)
        {
            Log.WriteLine($"Deleting directory '{path}'");

            var dir = new DirectoryInfo(path) { Attributes = FileAttributes.Normal };
            foreach (var info in dir.GetFileSystemInfos("*", SearchOption.AllDirectories))
            {
                info.Attributes = FileAttributes.Normal;
            }
            dir.Delete(recursive: true);
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
                if (e != null && e.Data != null)
                {
                    Log.WriteLine(e.Data);

                    if (job.State == ServerState.Starting && e.Data.Contains("Application started"))
                    {
                        job.State = ServerState.Running;
                        job.Url = $"http://{hostname}:5000/{job.Scenario.ToString().ToLower()}";
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
