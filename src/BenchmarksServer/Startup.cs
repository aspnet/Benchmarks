// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Benchmarks.ServerJob;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Repository;

namespace BenchmarkServer
{
    public class Startup
    {
        private const string _benchmarksRepoUrl = "https://github.com/aspnet/benchmarks.git";
        private static readonly Source _benchmarksSource = new Source() { Repository = _benchmarksRepoUrl };

        private const string _defaultUrl = "http://*:5001";
        private static readonly string _defaultHostname = Environment.MachineName.ToLowerInvariant();

        private static readonly IRepository<ServerJob> _jobs = new InMemoryRepository<ServerJob>();
        private static readonly bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

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
            string dotnetInstallDir = null;
            try
            {
                dotnetInstallDir = GetTempDir();

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
                                if (!_isWindows && job.WebHost != WebHost.Kestrel)
                                {
                                    Log.WriteLine($"Skipping job '{job.Id}' with scenario '{job.Scenario}'.");
                                    Log.WriteLine($"'{job.WebHost}' is not supported on this platform.");
                                    job.State = ServerState.NotSupported;
                                    continue;
                                }

                                Log.WriteLine($"Starting job '{job.Id}' with scenario '{job.Scenario}'");
                                job.State = ServerState.Starting;

                                Debug.Assert(tempDir == null);
                                tempDir = GetTempDir();

                                var benchmarksDir = CloneRestoreAndBuild(tempDir, job, dotnetInstallDir);

                                Debug.Assert(process == null);
                                process = StartProcess(hostname, Path.Combine(tempDir, benchmarksDir), job, dotnetInstallDir);
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
                                if (_isWindows)
                                {
                                    ProcessUtil.Run("taskkill.exe", $"/f /t /pid {process.Id}", throwOnError: false);
                                }
                                else
                                {
                                    ProcessUtil.Run("pkill", "--signal SIGINT --full Benchmarks.dll", throwOnError: false);
                                }
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
            finally
            {
                if (dotnetInstallDir != null)
                {
                    DeleteDir(dotnetInstallDir);
                }
            }
        }

        private static string CloneRestoreAndBuild(string path, ServerJob job, string dotnetInstallDir)
        {
            // It's possible that the user specified a custom branch/commit for the benchmarks repo,
            // so we need to add that to the set of sources to restore if it's not already there.
            //
            // Note that this is also going to de-dupe the repos if the same one was specified twice at
            // the command-line (last first to support overrides).
            var repos = new HashSet<Source>(job.Sources, SourceRepoComparer.Instance);

            // This will no-op if 'benchmarks' was specified by the user.
            repos.Add(_benchmarksSource);

            // Clone
            string benchmarksDir = null;
            var dirs = new List<string>();
            foreach (var source in repos)
            {
                var dir = Git.Clone(path, source.Repository);
                if (SourceRepoComparer.Instance.Equals(source, _benchmarksSource))
                {
                    benchmarksDir = dir;
                }

                if (!string.IsNullOrEmpty(source.BranchOrCommit))
                {
                    Git.Checkout(Path.Combine(path, dir), source.BranchOrCommit);
                }
                dirs.Add(dir);
            }

            Debug.Assert(benchmarksDir != null);

            AddSourceDependencies(path, benchmarksDir, dirs);

            // Install latest SDK and runtime
            // * Use custom install dir to avoid changing the default install,  which is impossible if other processes
            //   are already using it.
            var benchmarksRoot = Path.Combine(path, benchmarksDir);
            var env = new Dictionary<string, string> { { "DOTNET_INSTALL_DIR", dotnetInstallDir } };
            if (_isWindows)
            {
                ProcessUtil.Run("cmd", "/c build.cmd /t:noop", workingDirectory: benchmarksRoot, environmentVariables: env);
            }
            else
            {
                ProcessUtil.Run("/usr/bin/env", "bash build.sh /t:noop", workingDirectory: benchmarksRoot,
                    environmentVariables: env);
            }

            // Build and Restore
            var benchmarksApp = Path.Combine(benchmarksRoot, "src", "Benchmarks");
            var dotnetExecutable = Path.Combine(dotnetInstallDir, "dotnet");

            // Project versions must be higher than package versions to resolve those dependencies to project ones as expected.
            // Passing VersionSuffix to restore will have it append that to the version of restored projects, making them
            // higher than packages references by the same name.
            ProcessUtil.Run(dotnetExecutable, "restore /p:VersionSuffix=zzzzz-99999", workingDirectory: benchmarksApp);
            ProcessUtil.Run(dotnetExecutable, $"build -c Release -f {GetTFM(job.Framework)}", workingDirectory: benchmarksApp);

            return benchmarksDir;
        }

        private static void AddSourceDependencies(string path, string benchmarksDir, IEnumerable<string> dirs)
        {
            var benchmarksProjectPath = Path.Combine(path, benchmarksDir, "src", "Benchmarks", "Benchmarks.csproj");
            var benchmarksProjectDocument = XDocument.Load(benchmarksProjectPath);

            var commonReferences = new XElement("ItemGroup");
            var netFrameworkReferences = new XElement("ItemGroup", new XAttribute("Condition", @" '$(TargetFramework)' == 'net46' "));
            var netCoreReferences = new XElement("ItemGroup", new XAttribute("Condition", @" '$(TargetFramework)' == 'netcoreapp2.0' "));

            foreach (var dir in dirs.Except(new[] { benchmarksDir }))
            {
                var projects = Directory.EnumerateFiles(Path.Combine(path, dir, "src"), "*.csproj", SearchOption.AllDirectories);

                foreach (var project in projects)
                {
                    var projectDocument = XDocument.Load(project);
                    var targetFrameworks = projectDocument.Root.Descendants("TargetFrameworks").FirstOrDefault();
                    var targetFramework = projectDocument.Root.Descendants("TargetFramework").FirstOrDefault();

                    if (targetFrameworks == null && targetFramework == null)
                    {
                        Log.WriteLine($"Project '{project}' not added as a source reference because it has no target frameworks.");
                        continue;
                    }

                    var reference = new XElement("ProjectReference", new XAttribute("Include", project));

                    if (targetFrameworks != null)
                    {
                        commonReferences.Add(reference);
                    }
                    else if (targetFramework.Value.StartsWith("net4"))
                    {
                        netFrameworkReferences.Add(reference);
                    }
                    else if (targetFramework.Value.StartsWith("netstandard"))
                    {
                        netCoreReferences.Add(reference);
                    }
                }
            }

            benchmarksProjectDocument.Root.Add(commonReferences);
            benchmarksProjectDocument.Root.Add(netFrameworkReferences);
            benchmarksProjectDocument.Root.Add(netCoreReferences);

            using (var stream = File.OpenWrite(benchmarksProjectPath))
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
            {
                benchmarksProjectDocument.Save(writer);
            }
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

            // Delete occasionally fails with the following exception:
            //
            // System.UnauthorizedAccessException: Access to the path 'Benchmarks.dll' is denied.
            //
            // If delete fails, retry once every second up to 10 times.
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    var dir = new DirectoryInfo(path) { Attributes = FileAttributes.Normal };
                    foreach (var info in dir.GetFileSystemInfos("*", SearchOption.AllDirectories))
                    {
                        info.Attributes = FileAttributes.Normal;
                    }
                    dir.Delete(recursive: true);
                    break;
                }
                catch (Exception e)
                {
                    Log.WriteLine($"Error deleting directory: {e.ToString()}");

                    if (i < 9)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        private static Process StartProcess(string hostname, string benchmarksRepo, ServerJob job, string dotnetInstallDir)
        {
            var workingDirectory = Path.Combine(benchmarksRepo, "src", "Benchmarks");
            var benchmarksBinaryName = $"Benchmarks{GetBinaryExtension(job.Framework)}";
            var benchmarksBinaryRelativePath = Path.Combine("bin", "Release", GetTFM(job.Framework), benchmarksBinaryName);
            var filename = job.Framework == Framework.Core ?
                Path.Combine(dotnetInstallDir, "dotnet") :
                Path.Combine(workingDirectory, benchmarksBinaryRelativePath);
            var arguments = (job.Framework == Framework.Core ? $"{benchmarksBinaryRelativePath}" : "") +
                    $" --nonInteractive true" +
                    $" --scenarios {job.Scenario}" +
                    $" --server {job.WebHost}" +
                    $" --server.urls {job.Scheme.ToString().ToLowerInvariant()}://{hostname}:5000";

            if (!string.IsNullOrEmpty(job.ConnectionFilter))
            {
                arguments += $" --connectionFilter {job.ConnectionFilter}";
            }

            if (job.KestrelTransport.HasValue)
            {
                arguments += $" --kestrelTransport {job.KestrelTransport.Value}";
            }

            if (job.KestrelThreadCount.HasValue)
            {
                arguments += $" --threadCount {job.KestrelThreadCount.Value}";
            }

            if (job.KestrelThreadPoolDispatching.HasValue)
            {
                arguments += $" --kestrelThreadPoolDispatching {job.KestrelThreadPoolDispatching.Value}";
            }

            Log.WriteLine($"Starting process '{filename} {arguments}'");

            var process = new Process()
            {
                StartInfo = {
                    FileName = filename,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
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
                        job.Url = ComputeServerUrl(hostname, job.Scheme, job.Scenario);
                        Log.WriteLine($"Running job '{job.Id}' with scenario '{job.Scenario}'");
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();

            return process;
        }

        private static string ComputeServerUrl(string hostname, Scheme scheme, Scenario scenario)
        {
            var scenarioName = scenario.ToString();
            var path = scenarioName;

            var field = scenario.GetType().GetTypeInfo().GetField(scenarioName);
            var pathAttribute = field.GetCustomAttribute<ScenarioPathAttribute>();
            if (pathAttribute != null)
            {
                Debug.Assert(pathAttribute.Paths.Length > 0);
                if (pathAttribute.Paths.Length == 1)
                {
                    path = pathAttribute.Paths[0].Trim('/');
                }
                else
                {
                    // Driver will choose between paths when more than one is available. The scenario name is not
                    // necessarily even one of the choices.
                    path = string.Empty;
                }
            }

            return $"{scheme.ToString().ToLowerInvariant()}://{hostname}:5000/{path.ToLower()}";
        }

        private static string GetRepoName(Source source)
        {
            // Attempt to parse a string like
            // - http://<host>.com/<user>/<repo>.git OR
            // - http://<host>.com/<user>/<repo>
            var repository = source.Repository;
            var lastSlash = repository.LastIndexOf('/');
            var dot = repository.LastIndexOf('.');

            if (lastSlash == -1)
            {
                throw new InvalidOperationException($"Couldn't parse repository name from {source.Repository}");
            }

            var start = lastSlash + 1; // +1 to skip over the slash.
            var name = dot > lastSlash ? repository.Substring(start, dot - start) : repository.Substring(start);
            return name;
        }

        private static string GetTFM(Framework framework)
        {
            return framework == Framework.Core ? "netcoreapp2.0" : "net46";
        }

        private static string GetBinaryExtension(Framework framework)
        {
            return framework == Framework.Core ? ".dll" : ".exe";
        }

        // Compares just the repository name
        private class SourceRepoComparer : IEqualityComparer<Source>
        {
            public static readonly SourceRepoComparer Instance = new SourceRepoComparer();

            public bool Equals(Source x, Source y)
            {
                return string.Equals(GetRepoName(x), GetRepoName(y), StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(Source obj)
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(GetRepoName(obj));
            }
        }
    }
}
