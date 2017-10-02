﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Benchmarks.ServerJob;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Repository;

using OperatingSystem = Benchmarks.ServerJob.OperatingSystem;

namespace BenchmarkServer
{
    public class Startup
    {
        private static readonly HttpClient _httpClient;
        private static readonly HttpClientHandler _httpClientHandler;
        private const string _buildToolsRepoUrl = "https://raw.githubusercontent.com/aspnet/BuildTools/dev/scripts/bootstrapper/";
        private static readonly string[] _buildToolsFiles = new string[] { "build.cmd", "build.sh", "run.cmd", "run.ps1", "run.sh" };
        private const string _defaultUrl = "http://*:5001";
        private static readonly string _defaultHostname = Environment.MachineName.ToLowerInvariant();

        private static readonly IRepository<ServerJob> _jobs = new InMemoryRepository<ServerJob>();
        private static readonly string _rootTempDir;

        public static OperatingSystem OperatingSystem { get; }
        public static Hardware Hardware { get; private set; }

        static Startup()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                OperatingSystem = OperatingSystem.Linux;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                OperatingSystem = OperatingSystem.Windows;
            }
            else
            {
                throw new InvalidOperationException($"Invalid OSPlatform: {RuntimeInformation.OSDescription}");
            }

            _rootTempDir = Path.GetTempFileName();
            File.Delete(_rootTempDir);
            Directory.CreateDirectory(_rootTempDir);
            Log.WriteLine($"Created root temp directory '{_rootTempDir}'");

            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => { return true; };
            _httpClient = new HttpClient(_httpClientHandler);

            Action shutdown = () =>
            {
                if (Directory.Exists(_rootTempDir))
                {
                    DeleteDir(_rootTempDir);
                }
            };

            // SIGTERM
            AssemblyLoadContext.GetLoadContext(typeof(Startup).GetTypeInfo().Assembly).Unloading +=
                context => shutdown();

            // SIGINT
            Console.CancelKeyPress +=
                (sender, eventArgs) => shutdown();
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();

            services.AddSingleton(_jobs);
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseMvc();

            // Register a default startup page to ensure the application is up
            app.Map("", builder =>
                builder.Run((context) =>
                {
                    return context.Response.WriteAsync("OK!");
                })
            );
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
            var hardwareOption = app.Option("--hardware", "Hardware (Cloud or Physical).  Required.",
                CommandOptionType.SingleValue);
            var databaseOption = app.Option("-d|--database", "Database (PostgreSQL or SqlServer).",
                CommandOptionType.SingleValue);
            var sqlConnectionStringOption = app.Option("-q|--sql",
                "Connection string of SQL Database used by Benchmarks app", CommandOptionType.SingleValue);

            app.OnExecute(() =>
            {
                if (Enum.TryParse(hardwareOption.Value(), ignoreCase: true, result: out Hardware hardware))
                {
                    Hardware = hardware;
                }
                else
                {
                    Console.WriteLine($"Option --{hardwareOption.LongName} <TYPE> is required. Available types:");
                    foreach (Hardware type in Enum.GetValues(typeof(Hardware)))
                    {
                        Console.WriteLine($"  {type}");
                    }
                    return 2;
                }

                var url = urlOption.HasValue() ? urlOption.Value() : _defaultUrl;
                var hostname = hostnameOption.HasValue() ? hostnameOption.Value() : _defaultHostname;

                Database? database = null;
                if (databaseOption.HasValue())
                {
                    if (Enum.TryParse(databaseOption.Value(), out Database databaseValue))
                    {
                        database = databaseValue;
                    }
                    else
                    {
                        Console.WriteLine($"Invalid value for option --{databaseOption.LongName}: '{databaseOption.Value()}'");
                        return 2;
                    }
                }


                return Run(url, hostname, database, sqlConnectionStringOption.Value()).Result;
            });

            return app.Execute(args);
        }

        private static async Task<int> Run(string url, string hostname, Database? database, string sqlConnectionString)
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
            var processJobsTask = ProcessJobs(hostname, database, sqlConnectionString, processJobsCts.Token);

            var completedTask = await Task.WhenAny(hostTask, processJobsTask);

            // Propagate exception (and exit process) if either task faulted
            await completedTask;

            // Host exited normally, so cancel job processor
            processJobsCts.Cancel();
            await processJobsTask;

            return 0;
        }

        private static async Task ProcessJobs(string hostname, Database? database, string sqlConnectionString, CancellationToken cancellationToken)
        {
            string dotnetHome = null;
            try
            {
                dotnetHome = GetTempDir();

                Process process = null;
                Timer timer = null;
                                
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
                                if (OperatingSystem != OperatingSystem.Windows && job.WebHost != WebHost.Kestrel)
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

                                var benchmarksDir = await CloneRestoreAndBuild(tempDir, job, dotnetHome);

                                Debug.Assert(process == null);
                                process = StartProcess(hostname, database, sqlConnectionString, Path.Combine(tempDir, benchmarksDir),
                                    job, dotnetHome);
                                
                                var startMonitorTime = DateTime.UtcNow;
                                var lastMonitorTime = startMonitorTime;
                                var oldCPUTime = new TimeSpan(0);

                                timer = new Timer(_ => 
                                {
                                    var now = DateTime.UtcNow;
                                    var newCPUTime = process.TotalProcessorTime;
                                    var elapsed = now.Subtract(lastMonitorTime).TotalMilliseconds;
                                    var cpu =  Math.Round((newCPUTime - oldCPUTime).TotalMilliseconds / (Environment.ProcessorCount *  elapsed) * 100);
                                    lastMonitorTime = now;
                                    oldCPUTime = newCPUTime;

                                    job.ServerCounters.Add(new ServerCounter 
                                    { 
                                        Elapsed = now - startMonitorTime,
                                        WorkingSet = process.WorkingSet64,
                                        CpuPercentage = cpu
                                    });

                                }, null, TimeSpan.FromTicks(0), TimeSpan.FromSeconds(1));
                            }
                            catch (Exception e)
                            {
                                Log.WriteLine($"Error starting job '{job.Id}': {e}");

                                job.State = ServerState.Failed;

                                CleanJob();

                                continue;
                            }
                        }
                        else if (job.State == ServerState.Deleting)
                        {
                            Log.WriteLine($"Deleting job '{job.Id}' with scenario '{job.Scenario}'");

                            CleanJob();
                        }

                        void CleanJob()
                        {
                            if (timer != null)
                            {
                                timer.Dispose();
                                timer = null;
                            }

                            if (process != null)
                            {
                                // TODO: Replace with managed xplat version of kill process tree
                                if (OperatingSystem == OperatingSystem.Windows)
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
                if (dotnetHome != null)
                {
                    DeleteDir(dotnetHome);
                }
            }
        }

        private static async Task<string> CloneRestoreAndBuild(string path, ServerJob job, string dotnetHome)
        {
            // It's possible that the user specified a custom branch/commit for the benchmarks repo,
            // so we need to add that to the set of sources to restore if it's not already there.
            //
            // Note that this is also going to de-dupe the repos if the same one was specified twice at
            // the command-line (last first to support overrides).
            var repos = new HashSet<Source>(job.ReferenceSources, SourceRepoComparer.Instance);

            repos.Add(job.Source);

            // Clone
            string benchmarkedDir = null;
            var dirs = new List<string>();
            foreach (var source in repos)
            {
                var dir = Git.Clone(path, source.Repository);
                if (SourceRepoComparer.Instance.Equals(source, job.Source))
                {
                    benchmarkedDir = dir;
                }

                if (!string.IsNullOrEmpty(source.BranchOrCommit))
                {
                    Git.Checkout(Path.Combine(path, dir), source.BranchOrCommit);
                }
                dirs.Add(dir);
            }

            Debug.Assert(benchmarkedDir != null);

            // on windows dotnet is installed into subdirectory of 'dotnetHome' so we have to append 'x64'
            string dotnetExeLocation = OperatingSystem == OperatingSystem.Windows
                ? Path.Combine(dotnetHome, "x64")
                : dotnetHome;

            var env = new Dictionary<string, string>
            {
                // for repos using the latest build tools from aspnet/BuildTools
                ["DOTNET_HOME"] = dotnetHome,
                // for backward compatibility with aspnet/KoreBuild
                ["DOTNET_INSTALL_DIR"] = dotnetHome,
                // temporary for custom compiler to find right dotnet
                ["PATH"] = dotnetExeLocation + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
            };

            // Source dependencies are always built using KoreBuild
            AddSourceDependencies(path, benchmarkedDir, job.Source.Project, dirs, env);

            Log.WriteLine("Downloading build tools");

            // Install latest SDK and runtime
            // * Use custom install dir to avoid changing the default install, which is impossible if other processes
            //   are already using it.
            var buildToolsPath = Path.Combine(path, "buildtools");
            
            await DownloadBuildTools(buildToolsPath);

            Log.WriteLine("Installing dotnet runtimes and sdk");

            // Computes the location of the benchmarked app
            var benchmarkedApp = Path.Combine(path, benchmarkedDir, Path.GetDirectoryName(job.Source.Project));

            // Defines which SDK will be installed. Using "" downloads the latest SDK.
            if (job.AspNetCoreVersion == "2.1.0-*")
            {
                env["KOREBUILD_DOTNET_VERSION"] = "";
            }
            else
            {
                env["KOREBUILD_DOTNET_VERSION"] = "2.0.0";
                
                // Generate a global.json file in the local repository to force which SDK the application is using.
                File.WriteAllText(Path.Combine(benchmarkedApp, "global.json"), "{ \"sdk\": { \"version\": \"2.0.0\" } }\"");
            }            

            if (OperatingSystem == OperatingSystem.Windows)
            {
                ProcessUtil.Run("cmd", "/c build.cmd /t:noop", 
                    workingDirectory: buildToolsPath, 
                    environmentVariables: env);
            }
            else
            {
                ProcessUtil.Run("/usr/bin/env", "bash build.sh /t:noop", 
                    workingDirectory: buildToolsPath,
                    environmentVariables: env);
            }

            // Build and Restore
            var dotnetExecutable = GetDotNetExecutable(dotnetHome);

            // Project versions must be higher than package versions to resolve those dependencies to project ones as expected.
            // Passing VersionSuffix to restore will have it append that to the version of restored projects, making them
            // higher than packages references by the same name.
            var buildParameters = $"/p:BenchmarksAspNetCoreVersion={job.AspNetCoreVersion} " +
                $"/p:BenchmarksNETStandardImplicitPackageVersion={job.AspNetCoreVersion} " +
                $"/p:BenchmarksNETCoreAppImplicitPackageVersion={job.AspNetCoreVersion} " +
                $"/p:BenchmarksRuntimeFrameworkVersion=2.0.0 ";

            ProcessUtil.Run(dotnetExecutable, $"restore /p:VersionSuffix=zzzzz-99999", 
                workingDirectory: benchmarkedApp, 
                environmentVariables: env);

            if (job.UseRuntimeStore)
            {
                ProcessUtil.Run(dotnetExecutable, $"build -c Release {buildParameters}",
                    workingDirectory: benchmarkedApp,
                    environmentVariables: env);
            }
            else
            {
                // This flag is necessary when using the .All metapackage
                buildParameters += " /p:PublishWithAspNetCoreTargetManifest=false";

                ProcessUtil.Run(dotnetExecutable, $"publish -c Release -o {Path.Combine(benchmarkedApp, "published")} {buildParameters}",
                    workingDirectory: benchmarkedApp,
                    environmentVariables: env);
            }

            return benchmarkedDir;
        }

        private static async Task DownloadBuildTools(string buildToolsPath)
        {
            if (!Directory.Exists(buildToolsPath))
            {
                Directory.CreateDirectory(buildToolsPath);
            }

            foreach(var file in _buildToolsFiles)
            {
                var content = await _httpClient.GetStringAsync(_buildToolsRepoUrl + file);
                File.WriteAllText(Path.Combine(buildToolsPath, file), content);
            }
        }

        private static void AddSourceDependencies(string path, string benchmarksDir, string benchmarksProject, IEnumerable<string> dirs, IDictionary<string, string> env)
        {
            
            var benchmarksProjectPath = Path.Combine(path, benchmarksDir, benchmarksProject);
            var benchmarksProjectDocument = XDocument.Load(benchmarksProjectPath);

            var commonReferences = new XElement("ItemGroup");
            var netFrameworkReferences = new XElement("ItemGroup", new XAttribute("Condition", @" '$(TargetFramework)' == 'net46' "));
            var netCoreReferences = new XElement("ItemGroup", new XAttribute("Condition", @" '$(TargetFramework)' == 'netcoreapp2.0' "));

            foreach (var dir in dirs.Except(new[] { benchmarksDir }))
            {
                var repoRoot = Path.Combine(path, dir);
                var projects = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories);

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

                InitializeSourceRepo(repoRoot, env);
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
         
        private static void InitializeSourceRepo(string repoRoot, IDictionary<string, string> env)
        {
            var initArgs = new List<string>();
            var repoProps = Path.Combine(repoRoot, "build", "repo.props");
            if (File.Exists(repoProps))
            {
                var props = XDocument.Load(repoProps);
                if (props.Root.Descendants("DotNetCoreRuntime").Any())
                {
                    initArgs.Add("/t:InstallDotNet");
                }

                if (props.Root.Descendants("PackageLineup").Any())
                {
                    initArgs.Add("/t:Pin");
                }
            }

            if (initArgs.Count > 0)
            {
                var args = string.Join(' ', initArgs);
                if (OperatingSystem == OperatingSystem.Windows)
                {
                    ProcessUtil.Run("cmd", "/c build.cmd " + args, workingDirectory: repoRoot, environmentVariables: env);
                }
                else
                {
                    ProcessUtil.Run("/usr/bin/env", "bash build.sh " + args, workingDirectory: repoRoot, environmentVariables: env);
                }
            }
        }

        private static string GetTempDir()
        {
            var temp = Path.Combine(_rootTempDir, Path.GetRandomFileName());
            if (Directory.Exists(temp))
            {
                // Retry
                return GetTempDir();
            }
            else
            {
                Directory.CreateDirectory(temp);
                Log.WriteLine($"Created temp directory '{temp}'");
                return temp;
            }
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
                    Log.WriteLine("SUCCESS");
                    break;
                }
                catch(DirectoryNotFoundException)
                {
                    Log.WriteLine("Nothing to do");
                    break;
                }
                catch (Exception e)
                {
                    Log.WriteLine($"Error deleting directory: {e.ToString()}");

                    if (i < 9)
                    {
                        Log.WriteLine("RETRYING");
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        private static string GetDotNetExecutable(string dotnetHome)
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(dotnetHome, RuntimeInformation.ProcessArchitecture.ToString(), "dotnet.exe")
                : Path.Combine(dotnetHome, "dotnet");
        }

        private static Process StartProcess(string hostname, Database? database, string sqlConnectionString, string benchmarksRepo,
            ServerJob job, string dotnetHome)
        {
            var serverUrl = $"{job.Scheme.ToString().ToLowerInvariant()}://{hostname}:{job.Port}";

            var dotnetFilename = GetDotNetExecutable(dotnetHome);
            var projectFilename = Path.GetFileNameWithoutExtension(job.Source.Project);
            var benchmarksDll = job.UseRuntimeStore ? $"bin/Release/netcoreapp2.0/{projectFilename}.dll" : $"published/{projectFilename}.dll";

            var arguments = $"{benchmarksDll}"+
                    $" {job.Arguments} " +
                    $" --nonInteractive true" +
                    $" --scenarios {job.Scenario}" +
                    $" --server {job.WebHost}" +
                    $" --server.urls {serverUrl}";

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

            Log.WriteLine($"Starting process '{dotnetFilename} {arguments}'");

            var process = new Process()
            {
                StartInfo = {
                    FileName = dotnetFilename,
                    Arguments = arguments,
                    WorkingDirectory = Path.Combine(benchmarksRepo, Path.GetDirectoryName(job.Source.Project)),
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
                EnableRaisingEvents = true
            };

            process.StartInfo.Environment.Add("COREHOST_SERVER_GC", "1");

            // Force Kestrel server urls
            process.StartInfo.Environment.Add("ASPNETCORE_URLS", serverUrl);
            

            if (database.HasValue)
            {
                process.StartInfo.Environment.Add("Database", database.Value.ToString());
            }

            if (!string.IsNullOrEmpty(sqlConnectionString))
            {
                process.StartInfo.Environment.Add("ConnectionString", sqlConnectionString);
            }

            var stopwatch = new Stopwatch();
            
            process.OutputDataReceived += (_, e) =>
            {
                if (e != null && e.Data != null)
                {
                    Log.WriteLine(e.Data);

                    if (job.State == ServerState.Starting && (e.Data.ToLowerInvariant().Contains("started") || e.Data.ToLowerInvariant().Contains("listening")) )
                    {
                        job.StartupMainMethod = stopwatch.Elapsed;

                        Log.WriteLine($"Running job '{job.Id}' with scenario '{job.Scenario}'");
                        job.Url = ComputeServerUrl(hostname, job);

                        // Mark the job as running to allow the Client to start the test
                        job.State = ServerState.Running;
                    }
                }
            };

            stopwatch.Start();
            process.Start();
            process.BeginOutputReadLine();

            return process;
        }

        private static string ComputeServerUrl(string hostname, ServerJob job)
        {
            return $"{job.Scheme.ToString().ToLowerInvariant()}://{hostname}:{job.Port}/{job.Path.TrimStart('/')}";
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
