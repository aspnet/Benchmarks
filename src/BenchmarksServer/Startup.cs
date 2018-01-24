// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
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
        private static readonly string _dotnetInstallRepoUrl = "https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/";
        private static readonly string[] _dotnetInstallPaths = new string[] { "dotnet-install.sh", "dotnet-install.ps1" };
        private static readonly string _sdkVersionUrl = "https://raw.githubusercontent.com/aspnet/BuildTools/dev/files/KoreBuild/config/sdk.version";
        private static readonly string _universeDependenciesUrl = "https://raw.githubusercontent.com/aspnet/Universe/dev/build/dependencies.props";
        private static readonly string _perfviewUrl = "https://github.com/Microsoft/perfview/releases/download/P2.0.2/PerfView.exe";

        // Cached lists of SDKs and runtimes already installed
        private static readonly List<string> _installedRuntimes = new List<string>();
        private static readonly List<string> _installedSdks = new List<string>();

        private const string _defaultUrl = "http://*:5001";
        private static readonly string _defaultHostname = Environment.MachineName.ToLowerInvariant();
        private static readonly string _perfviewPath;
        private static readonly IRepository<ServerJob> _jobs = new InMemoryRepository<ServerJob>();
        private static readonly string _rootTempDir;
        private static bool _cleanup = true;

        public static OperatingSystem OperatingSystem { get; }
        public static Hardware Hardware { get; private set; }
        public static string HardwareVersion { get; private set; }
        public static Dictionary<Database, string> ConnectionStrings = new Dictionary<Database, string>();

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
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                OperatingSystem = OperatingSystem.OSX;
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
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClient = new HttpClient(_httpClientHandler);

            // Download PerfView
            if (OperatingSystem == OperatingSystem.Windows)
            {
                _perfviewPath = Path.Combine(GetTempDir(), Path.GetFileName(_perfviewUrl));
                Log.WriteLine($"Downloading PerfView to '{_perfviewPath}'");
                DownloadFileAsync(_perfviewUrl, _perfviewPath, maxRetries: 5, timeout: 60).GetAwaiter().GetResult();
            }

            Action shutdown = () =>
            {
                if (_cleanup && Directory.Exists(_rootTempDir))
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
            app.Run((context) =>
            {
                return context.Response.WriteAsync("OK!");
            });
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
            var hardwareVersionOption = app.Option("--hardware-version", "Hardware version (e.g, D3V2, Z420, ...).  Required.",
                CommandOptionType.SingleValue);
            var databaseOption = app.Option("-d|--database", "Database (PostgreSQL, SqlServer, MySql or MongoDb).",
                CommandOptionType.SingleValue);
            var noCleanupOption = app.Option("--no-cleanup",
                "Don't kill processes or delete temp directories.", CommandOptionType.NoValue);
            var postgresqlConnectionStringOption = app.Option("--postgresql",
                "The connection string for PostgreSql.", CommandOptionType.SingleValue);
            var mysqlConnectionStringOption = app.Option("--mysql",
                "The connection string for MySql.", CommandOptionType.SingleValue);
            var mssqlConnectionStringOption = app.Option("--mssql",
                "The connection string for SqlServer.", CommandOptionType.SingleValue);
            var mongoDbConnectionStringOption = app.Option("--mongodb",
                "The connection string for MongoDb.", CommandOptionType.SingleValue);

            app.OnExecute(() =>
            {
                if (noCleanupOption.HasValue())
                {
                    _cleanup = false;
                }
                
                if (postgresqlConnectionStringOption.HasValue())
                {
                    ConnectionStrings[Database.PostgreSql] = postgresqlConnectionStringOption.Value();
                }
                if (mysqlConnectionStringOption.HasValue())
                {
                    ConnectionStrings[Database.MySql] = mysqlConnectionStringOption.Value();
                }
                if (mssqlConnectionStringOption.HasValue())
                {
                    ConnectionStrings[Database.SqlServer] = mssqlConnectionStringOption.Value();
                }
                if (mongoDbConnectionStringOption.HasValue())
                {
                    ConnectionStrings[Database.MongoDb] = mongoDbConnectionStringOption.Value();
                }
                if (hardwareVersionOption.HasValue() && !string.IsNullOrWhiteSpace(hardwareVersionOption.Value()))
                {
                    HardwareVersion = hardwareVersionOption.Value();
                }
                else
                {
                    Console.WriteLine("Option --hardware-version is required.");
                    return 3;
                }
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


                return Run(url, hostname).Result;
            });

            return app.Execute(args);
        }

        private static async Task<int> Run(string url, string hostname)
        {
            var host = new WebHostBuilder()
                    .UseKestrel()
                    .UseStartup<Startup>()
                    .UseUrls(url)
                    .Build();

            var hostTask = host.RunAsync();

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
            string dotnetHome = null;
            try
            {
                dotnetHome = GetTempDir();

                Process process = null;
                Timer timer = null;
                var executionLock = new object();
                var disposed = false;

                string tempDir = null;
                string dockerImage = null;
                string dockerContainerId = null;

                while (!cancellationToken.IsCancellationRequested)
                {
                    var allJobs = _jobs.GetAll();
                    var job = allJobs.FirstOrDefault();
                    if (job != null)
                    {
                        string dotnetDir = dotnetHome;
                        string benchmarksDir = null;

                        var perfviewEnabled = job.Collect && OperatingSystem == OperatingSystem.Windows;

                        if (job.State == ServerState.Waiting)
                        {
                            // TODO: Race condition if DELETE is called during this code
                            try
                            {
                                if (OperatingSystem != OperatingSystem.Windows && job.WebHost != WebHost.KestrelSockets && job.WebHost != WebHost.KestrelLibuv)
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

                                if (job.Source.DockerFile != null)
                                {
                                    (dockerContainerId, dockerImage) = await DockerBuildAndRun(tempDir, job, hostname);
                                }
                                else
                                {
                                    // returns the application directory and the dotnet directory to use
                                    (benchmarksDir, dotnetDir) = await CloneRestoreAndBuild(tempDir, job, dotnetDir);

                                    Debug.Assert(process == null);
                                    process = StartProcess(hostname, Path.Combine(tempDir, benchmarksDir), job, dotnetDir, perfviewEnabled);
                                }

                                var startMonitorTime = DateTime.UtcNow;
                                var lastMonitorTime = startMonitorTime;
                                var oldCPUTime = TimeSpan.Zero;

                                disposed = false;

                                timer = new Timer(_ =>
                                {
                                    // If we couldn't get the lock it means one of 2 things are true:
                                    // - We're about to dispose so we don't care to run the scan callback anyways.
                                    // - The previous the computation took long enough that the next scan tried to run in parallel
                                    // In either case just do nothing and end the timer callback as soon as possible
                                    if (!Monitor.TryEnter(executionLock))
                                    {
                                        return;
                                    }

                                    try
                                    {
                                        if (disposed)
                                        {
                                            return;
                                        }

                                        // Pause the timer while we're running
                                        timer.Change(Timeout.Infinite, Timeout.Infinite);

                                        var now = DateTime.UtcNow;

                                        // Clean the job in case the client job is not running
                                        if (now - startMonitorTime > job.Timeout)
                                        {
                                            Log.WriteLine($"Job timed out after {job.Timeout}. Halting job.");
                                            job.State = ServerState.Deleting;
                                        }

                                        // Clean the job in case the driver is not running
                                        if (now - job.LastDriverCommunicationUtc > TimeSpan.FromSeconds(30))
                                        {
                                            Log.WriteLine($"Driver didn't communicate for {now - job.LastDriverCommunicationUtc}. Halting job.");
                                            job.State = ServerState.Deleting;
                                        }

                                        if (process != null)
                                        {
                                            // TODO: Accessing the TotalProcessorTime on OSX throws so just leave it as 0 for now
                                            // We need to dig into this
                                            var newCPUTime = OperatingSystem == OperatingSystem.OSX ? TimeSpan.Zero : process.TotalProcessorTime;
                                            var elapsed = now.Subtract(lastMonitorTime).TotalMilliseconds;
                                            var cpu = Math.Round((newCPUTime - oldCPUTime).TotalMilliseconds / (Environment.ProcessorCount * elapsed) * 100);
                                            lastMonitorTime = now;
                                            oldCPUTime = newCPUTime;

                                            job.ServerCounters.Add(new ServerCounter
                                            {
                                                Elapsed = now - startMonitorTime,
                                                WorkingSet = process.WorkingSet64,
                                                CpuPercentage = cpu
                                            });
                                        }
                                        else
                                        {
                                            // Get docker stats
                                            var result = ProcessUtil.Run("docker", "container stats --no-stream --format \"{{.CPUPerc}}-{{.MemUsage}}\" " + dockerContainerId);
                                            var data = result.StandardOutput.Trim().Split('-');

                                            // Format is {value}%
                                            var cpuPercentRaw = data[0];

                                            // Format is {used}M/GiB/{total}M/GiB
                                            var workingSetRaw = data[1];
                                            var usedMemoryRaw = workingSetRaw.Split('/')[0].Trim();
                                            var cpu = double.Parse(cpuPercentRaw.Trim('%'));

                                            // MiB or GiB
                                            var factor = usedMemoryRaw.EndsWith("MiB") ? 1024 * 1024 : 1024 * 1024 * 1024;
                                            var memory = double.Parse(usedMemoryRaw.Substring(0, usedMemoryRaw.Length - 3));
                                            var workingSet = (long)(memory * factor);

                                            job.ServerCounters.Add(new ServerCounter
                                            {
                                                Elapsed = now - startMonitorTime,
                                                WorkingSet = workingSet,
                                                CpuPercentage = cpu
                                            });
                                        }

                                        // Resume once we finished processing all connections
                                        timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                                    }
                                    finally
                                    {
                                        // Exit the lock now
                                        Monitor.Exit(executionLock);
                                    }
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
                        else if (job.State == ServerState.TraceCollecting)
                        {
                            // Collection perfview results
                            if (perfviewEnabled)
                            {
                                // Start perfview
                                var perfviewArguments = $"stop /AcceptEula /NoNGenRundown /NoRundown /NoView";
                                var perfViewProcess = RunPerfview(perfviewArguments, benchmarksDir);
                                job.State = ServerState.TraceCollected;
                            }
                        }

                        void CleanJob()
                        {
                            lock (executionLock)
                            {
                                timer?.Dispose();
                                timer = null;

                                disposed = true;
                            }

                            if (process != null)
                            {
                                // TODO: Replace with managed xplat version of kill process tree
                                if (OperatingSystem == OperatingSystem.Windows)
                                {
                                    ProcessUtil.Run("taskkill.exe", $"/f /t /pid {process.Id}", throwOnError: false);
                                }
                                else if (OperatingSystem == OperatingSystem.OSX)
                                {
                                    // pkill isn't available on OSX and this will only kill bash probably
                                    ProcessUtil.Run("kill", $"-s SIGINT {process.Id}", throwOnError: false);
                                }
                                else
                                {
                                    var assemblyName = Path.GetFileNameWithoutExtension(job.Source.Project);
                                    ProcessUtil.Run("pkill", $"--signal SIGINT --full {assemblyName}.dll", throwOnError: false);
                                }
                                process.Dispose();
                                process = null;

                                if (perfviewEnabled)
                                {
                                    // Abort all perfview processes
                                    var perfViewProcess = RunPerfview("abort", Path.GetPathRoot(_perfviewPath));
                                }
                            }
                            else if (dockerImage != null)
                            {
                                DockerCleanUp(dockerContainerId, dockerImage);
                            }

                            if (_cleanup && tempDir != null)
                            {
                                DeleteDir(tempDir);
                            }

                            // If a custom dotnet directory was used, clean it
                            if (_cleanup && dotnetDir != dotnetHome)
                            {
                                DeleteDir(dotnetDir);
                            }

                            tempDir = null;

                            _jobs.Remove(job.Id);
                        }
                    }
                    await Task.Delay(100);
                }
            }
            finally
            {
                if (_cleanup && dotnetHome != null)
                {
                    DeleteDir(dotnetHome);
                }
            }
        }

        private static string RunPerfview(string arguments, string workingDirectory)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Log.WriteLine($"PerfView is only supported on Windows");
                return null;
            }

            Log.WriteLine($"Starting process '{_perfviewPath} {arguments}'");

            var process = new Process()
            {
                StartInfo = {
                    FileName = _perfviewPath,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                },
                EnableRaisingEvents = true
            };

            var perfviewDoneEvent = new ManualResetEvent(false);
            var output = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e != null && e.Data != null)
                {
                    Log.WriteLine(e.Data);

                    if (e.Data.Contains("Press enter to close window"))
                    {
                        perfviewDoneEvent.Set();
                    }

                    output.Append(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();

            // Wait until PerfView is done
            perfviewDoneEvent.WaitOne();

            // Perfview is waiting for a keystroke to stop
            process.StandardInput.WriteLine();

            process.Close();
            return output.ToString();
        }

        private static async Task<(string containerId, string imageName)> DockerBuildAndRun(string path, ServerJob job, string hostname)
        {
            var source = job.Source;
            // Docker image names must be lowercase
            var imageName = $"benchmarks_{Path.GetDirectoryName(source.Project)}".ToLowerInvariant();
            var cloneDir = Path.Combine(path, Git.Clone(path, source.Repository));

            if (!string.IsNullOrEmpty(source.BranchOrCommit))
            {
                Git.Checkout(cloneDir, source.BranchOrCommit);
            }

            ProcessUtil.Run("docker", $"build -t {imageName} -f {source.DockerFile} .", workingDirectory: cloneDir);

            // Only run on the host network on linux
            var useHostNetworking = OperatingSystem == OperatingSystem.Linux;

            var command = useHostNetworking ? $"run -d --rm --network host {imageName}" :
                                              $"run -d --rm -p {job.Port}:{job.Port} {imageName}";
            var result = ProcessUtil.Run("docker", $"{command} {job.Arguments}");
            var containerId = result.StandardOutput.Trim();
            var url = ComputeServerUrl(hostname, job);

            // Wait until the service is reachable to avoid races where the container started but isn't
            // listening yet. We only try 5 times, if it keeps failing we ignore it. If the port
            // is unreachable then clients will fail to connect and the job will be cleaned up properl
            const int maxRetries = 5;
            for (var i = 0; i < maxRetries; ++i)
            {
                try
                {
                    // We don't care if it's a 404, it just needs to not fail
                    await _httpClient.GetAsync(url);
                    break;
                }
                catch
                {
                    await Task.Delay(300);
                }
            }


            Log.WriteLine($"Running job '{job.Id}' with scenario '{job.Scenario}' in container {containerId}");

            job.Url = url;
            job.State = ServerState.Running;

            return (containerId, imageName);
        }

        private static void DockerCleanUp(string containerId, string imageName)
        {
            var result = ProcessUtil.Run("docker", $"logs {containerId}");
            Console.WriteLine(result.StandardOutput);

            ProcessUtil.Run("docker", $"stop {containerId}");

            ProcessUtil.Run("docker", $"rmi {imageName}");
        }

        private static async Task<(string benchmarkDir, string dotnetDir)> CloneRestoreAndBuild(string path, ServerJob job, string dotnetHome)
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

            var env = new Dictionary<string, string>
            {
                // for repos using the latest build tools from aspnet/BuildTools
                ["DOTNET_HOME"] = dotnetHome,
                // for backward compatibility with aspnet/KoreBuild
                ["DOTNET_INSTALL_DIR"] = dotnetHome,
                // temporary for custom compiler to find right dotnet
                ["PATH"] = dotnetHome + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
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

            // Downloading latest SDK version
            var sdkVersionPath = Path.Combine(buildToolsPath, Path.GetFileName(_sdkVersionUrl));
            await DownloadFileAsync(_sdkVersionUrl, sdkVersionPath, maxRetries: 5);

            // var sdkVersion = File.ReadAllText(sdkVersionPath);
            // Log.WriteLine($"Detecting latest SDK version: {sdkVersion}");

            // This is the last known working SDK with Benchmarks on Linux
            var sdkVersion = "2.2.0-preview1-007522";
            Log.WriteLine($"WARNING !!! CHANGE WHEN FIXED");
            Log.WriteLine($"Using last known compatible SDK: {sdkVersion}");

            // In theory the actual latest runtime version should be taken from the dependencies.pros file from 
            // https://dotnet.myget.org/feed/aspnetcore-dev/package/nuget/Internal.AspNetCore.Universe.Lineup
            // however this is different only if the coherence build didn't go through.

            // Define which Runtime and SDK will be installed.

            string targetFramework;
            string runtimeFrameworkVersion;
            string aspNetCoreVersion;

            if (job.RuntimeVersion != "Current")
            {
                env["KOREBUILD_DOTNET_VERSION"] = ""; // Using "" downloads the latest SDK.
                File.WriteAllText(Path.Combine(benchmarkedApp, "global.json"), "{  \"sdk\": { \"version\": \"" + sdkVersion + "\" } }");
                targetFramework = "netcoreapp2.1";
                aspNetCoreVersion = "2.1-*";

                if (job.RuntimeVersion == "Latest")
                {
                    runtimeFrameworkVersion = await GetLatestRuntimeVersion(buildToolsPath);
                }
                else
                {
                    // Custom version
                    runtimeFrameworkVersion = job.RuntimeVersion;
                }
            }
            else
            {
                // Latest public version
                env["KOREBUILD_DOTNET_VERSION"] = "2.0.0";
                File.WriteAllText(Path.Combine(benchmarkedApp, "global.json"), "{  \"sdk\": { \"version\": \"" + sdkVersion + "\" } }");
                runtimeFrameworkVersion = "2.0.3";
                aspNetCoreVersion = "2.0-*";
                targetFramework = "netcoreapp2.0";
            }

            // Define which ASP.NET Core packages version to use
            switch(job.AspNetCoreVersion)
            {
                case "Current":
                    aspNetCoreVersion = "2.0-*";
                    break;
                case "Latest":
                    aspNetCoreVersion = "2.1-*";
                    break;
                default:
                    aspNetCoreVersion = job.AspNetCoreVersion;
                    break;
            }


            if (OperatingSystem == OperatingSystem.Windows)
            {
                if (!_installedRuntimes.Contains("Current"))
                {
                    // Install latest stable 2.0 SDK version (and associated runtime)
                    ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted .\\dotnet-install.ps1 -Channel Current",
                        workingDirectory: buildToolsPath,
                        environmentVariables: env);

                    _installedRuntimes.Add("Current");
                }

                if (!_installedSdks.Contains(sdkVersion))
                {
                    // Install latest SDK version (and associated runtime)
                    ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted .\\dotnet-install.ps1 -Version {sdkVersion}",
                    workingDirectory: buildToolsPath,
                    environmentVariables: env);

                    _installedSdks.Add(sdkVersion);
                }

                if (!_installedRuntimes.Contains(runtimeFrameworkVersion))
                {
                    // Install runtime required for this scenario
                    ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted .\\dotnet-install.ps1 -Version {runtimeFrameworkVersion} -SharedRuntime",
                    workingDirectory: buildToolsPath,
                    environmentVariables: env);

                    _installedRuntimes.Add(runtimeFrameworkVersion);
                }
            }
            else
            {
                if (!_installedRuntimes.Contains("Current"))
                {
                    // Install latest stable 2.0 SDK version (and associated runtime)
                    ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --channel Current",
                    workingDirectory: buildToolsPath,
                    environmentVariables: env);
                    _installedRuntimes.Add("Current");
                }

                if (!_installedSdks.Contains(sdkVersion))
                {
                    // Install latest SDK version (and associated runtime)
                    ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --version {sdkVersion}",
                    workingDirectory: buildToolsPath,
                    environmentVariables: env);
                    _installedSdks.Add(sdkVersion);
                }

                if (!_installedRuntimes.Contains(runtimeFrameworkVersion))
                {
                    // Install runtime required by coherence universe
                    ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --version {runtimeFrameworkVersion} --shared-runtime",
                    workingDirectory: buildToolsPath,
                    environmentVariables: env);

                    _installedRuntimes.Add(runtimeFrameworkVersion);
                }
            }

            var dotnetDir = dotnetHome;

            // If there is no custom runtime attachment we don't need to copy the dotnet folder
            if (job.Attachments.Any(x => x.Location == AttachmentLocation.Runtime))
            {
                dotnetDir = GetTempDir();

                Log.WriteLine($"Cloning dotnet folder for customization in {dotnetDir}");
                CloneDir(dotnetHome, dotnetDir);
            }

            // Build and Restore
            var dotnetExecutable = GetDotNetExecutable(dotnetDir);

            // Project versions must be higher than package versions to resolve those dependencies to project ones as expected.
            // Passing VersionSuffix to restore will have it append that to the version of restored projects, making them
            // higher than packages references by the same name.
            var buildParameters = $"/p:BenchmarksAspNetCoreVersion={aspNetCoreVersion} " +
                $"/p:BenchmarksNETStandardImplicitPackageVersion={aspNetCoreVersion} " +
                $"/p:BenchmarksNETCoreAppImplicitPackageVersion={aspNetCoreVersion} " +
                $"/p:BenchmarksRuntimeFrameworkVersion={runtimeFrameworkVersion} " +
                $"/p:BenchmarksTargetFramework={targetFramework} ";

            ProcessUtil.Run(dotnetExecutable, $"restore /p:VersionSuffix=zzzzz-99999 {buildParameters}",
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

                // This flag is necessary when using the .All metapackage since ASP.NET shared runtime 2.1
                buildParameters += " /p:MicrosoftNETPlatformLibrary=Microsoft.NETCore.App";

                var outputFolder = Path.Combine(benchmarkedApp, "published");

                ProcessUtil.Run(dotnetExecutable, $"publish -c Release -o {outputFolder} {buildParameters}",
                    workingDirectory: benchmarkedApp,
                    environmentVariables: env);

                // Copy all output attachments
                foreach (var attachment in job.Attachments)
                {
                    if (attachment.Location == AttachmentLocation.Output)
                    {
                        var filename = Path.Combine(outputFolder, attachment.Filename.Replace("\\", "/"));

                        Log.WriteLine($"Creating output file: {filename}");

                        if (File.Exists(filename))
                        {
                            File.Delete(filename);
                        }

                        await File.WriteAllBytesAsync(filename, attachment.Content);
                    }
                }
            }

            // Copy all runtime attachments in all runtime folders
            foreach (var attachment in job.Attachments)
            {
                var runtimeRoot = Path.Combine(dotnetDir, "shared", "Microsoft.NETCore.App");

                foreach (var runtimeFolder in Directory.GetDirectories(runtimeRoot))
                {
                    if (attachment.Location == AttachmentLocation.Runtime)
                    {
                        var filename = Path.Combine(runtimeFolder, attachment.Filename.Replace("\\", "/"));

                        Log.WriteLine($"Creating runtime file: {filename}");

                        if (File.Exists(filename))
                        {
                            File.Delete(filename);
                        }

                        await File.WriteAllBytesAsync(filename, attachment.Content);
                    }
                }
            }

            return (benchmarkedDir, dotnetDir);
        }

        private static async Task<string> GetLatestRuntimeVersion(string buildToolsPath)
        {
            var universeDependenciesPath = Path.Combine(buildToolsPath, Path.GetFileName(_universeDependenciesUrl));
            await DownloadFileAsync(_universeDependenciesUrl, universeDependenciesPath, maxRetries: 5);
            var latestRuntimeVersion = XDocument.Load(universeDependenciesPath).Root
                .Element("PropertyGroup")
                .Element("MicrosoftNETCoreApp21PackageVersion")
                .Value;
            Log.WriteLine($"Detecting Universe Coherence runtime version: {latestRuntimeVersion}");
            return latestRuntimeVersion;
        }

        private static async Task DownloadBuildTools(string buildToolsPath)
        {
            if (!Directory.Exists(buildToolsPath))
            {
                Log.WriteLine("Creating build tools folder");

                Directory.CreateDirectory(buildToolsPath);
            }

            const int maxRetries = 5;

            foreach (var file in _dotnetInstallPaths)
            {
                var url = _dotnetInstallRepoUrl + file;

                // If any of the files completely fails to download the entire thing will fail
                var path = Path.Combine(buildToolsPath, file);
                await DownloadFileAsync(url, path, maxRetries);
            }
        }

        private static async Task DownloadFileAsync(string url, string outputPath, int maxRetries, int timeout = 5)
        {
            Log.WriteLine($"Downloading {url}");

            for (var i = 0; i < maxRetries; ++i)
            {
                try
                {
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
                    var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);
                    response.EnsureSuccessStatusCode();

                    // This probably won't use async IO on windows since the stream
                    // needs to created with the right flags
                    using (var stream = File.Create(outputPath))
                    {
                        // Copy the response stream directly to the file stream
                        await response.Content.CopyToAsync(stream);
                    }

                    return;
                }
                catch (OperationCanceledException)
                {
                    Log.WriteLine($"Timeout trying to download {url}, attempt {i + 1}");
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Failed to download {url}, attempt {i + 1}, Exception: {ex}");
                }
            }

            throw new InvalidOperationException($"Failed to download {url} after {maxRetries} attempts");
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

                    // If the project contains "<IncludeBuildOutput>false</IncludeBuildOutput>", adding it as a
                    // source reference will likely cause a build error.
                    var includeBuildOutput = projectDocument.Root.Descendants("IncludeBuildOutput").FirstOrDefault();
                    if (includeBuildOutput != null && bool.Parse(includeBuildOutput.Value) == false)
                    {
                        Log.WriteLine($"Project '{project}' not added as a source reference because includeBuildOutput=false.");
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

        private static void CloneDir(string source, string dest)
        {
            foreach (string dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(source, dest));
            }

            // Copy all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(source, dest), true);
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
                catch (DirectoryNotFoundException)
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
                ? Path.Combine(dotnetHome, "dotnet.exe")
                : Path.Combine(dotnetHome, "dotnet");
        }

        private static Process StartProcess(string hostname, string benchmarksRepo, ServerJob job, string dotnetHome, bool perfview)
        {
            var serverUrl = $"{job.Scheme.ToString().ToLowerInvariant()}://{hostname}:{job.Port}";
            var dotnetFilename = GetDotNetExecutable(dotnetHome);
            var projectFilename = Path.GetFileNameWithoutExtension(job.Source.Project);
            var benchmarksDll = job.UseRuntimeStore ? $"bin/Release/netcoreapp2.0/{projectFilename}.dll" : $"published/{projectFilename}.dll";

            var arguments = $"{benchmarksDll}" +
                    $" {job.Arguments} " +
                    $" --nonInteractive true" +
                    $" --scenarios {job.Scenario}" +
                    $" --server.urls {serverUrl}";

            if (!string.IsNullOrEmpty(job.ConnectionFilter))
            {
                arguments += $" --connectionFilter {job.ConnectionFilter}";
            }

            switch (job.WebHost)
            {
                case WebHost.HttpSys:
                    arguments += $" --server HttpSys";
                    break;
                case WebHost.KestrelSockets:
                    arguments += $" --server Kestrel --kestrelTransport Sockets";
                    break;
                case WebHost.KestrelLibuv:
                    arguments += $" --server Kestrel --kestrelTransport Libuv";
                    break;
            }

            if (job.KestrelThreadCount.HasValue)
            {
                arguments += $" --threadCount {job.KestrelThreadCount.Value}";
            }

            Log.WriteLine($"Starting process '{dotnetFilename} {arguments}'");

            var benchmarksDir = Path.Combine(benchmarksRepo, Path.GetDirectoryName(job.Source.Project));

            var process = new Process()
            {
                StartInfo = {
                    FileName = dotnetFilename,
                    Arguments = arguments,
                    WorkingDirectory = benchmarksDir,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
                EnableRaisingEvents = true
            };

            process.StartInfo.Environment.Add("COREHOST_SERVER_GC", "1");

            // Force Kestrel server urls
            process.StartInfo.Environment.Add("ASPNETCORE_URLS", serverUrl);

            if (job.Database != Database.None)
            {
                if (ConnectionStrings.ContainsKey(job.Database))
                {
                    process.StartInfo.Environment.Add("Database", job.Database.ToString());
                    process.StartInfo.Environment.Add("ConnectionString", ConnectionStrings[job.Database]);
                }
                else
                {
                    Log.WriteLine($"Could not find connection string for {job.Database}");
                }
            }
                        
            var stopwatch = new Stopwatch();

            process.OutputDataReceived += (_, e) =>
            {
                if (e != null && e.Data != null)
                {
                    Log.WriteLine(e.Data);

                    if (job.State == ServerState.Starting && (e.Data.ToLowerInvariant().Contains("started") || e.Data.ToLowerInvariant().Contains("listening")))
                    {
                        job.StartupMainMethod = stopwatch.Elapsed;

                        Log.WriteLine($"Running job '{job.Id}' with scenario '{job.Scenario}'");
                        job.Url = ComputeServerUrl(hostname, job);

                        // Start perfview?
                        if (perfview)
                        {
                            job.PerfViewTraceFile = Path.Combine(benchmarksDir, "benchmarks.etl");
                            var perfViewArguments = new Dictionary<string, string>();
                            perfViewArguments["AcceptEula"] = "";
                            perfViewArguments["NoGui"] = "";
                            perfViewArguments["BufferSize"] = "256";
                            perfViewArguments["Process"] = process.Id.ToString();

                            if (!String.IsNullOrEmpty(job.CollectArguments))
                            {
                                foreach (var tuple in job.CollectArguments.Split(';'))
                                {
                                    var values = tuple.Split('=');
                                    perfViewArguments[values[0]] = values.Length > 1 ? values[1] : "";
                                }
                            }

                            var perfviewArguments = $"start";

                            foreach (var customArg in perfViewArguments)
                            {
                                var value = String.IsNullOrEmpty(customArg.Value) ? "" : $"={customArg.Value}";
                                perfviewArguments += $" /{customArg.Key}{value}";
                            }

                            perfviewArguments += $" \"{job.PerfViewTraceFile}\"";
                            RunPerfview(perfviewArguments, Path.Combine(benchmarksRepo, benchmarksDir));
                        }

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
