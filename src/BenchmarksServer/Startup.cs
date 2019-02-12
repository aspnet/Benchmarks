// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using Benchmarks.ServerJob;
using BenchmarksServer;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Repository;
using OperatingSystem = Benchmarks.ServerJob.OperatingSystem;

namespace BenchmarkServer
{
    public class Startup
    {

        /*
         * List of accepted values for AspNetCoreVersion and RuntimeVersion
         * 
            Current The latest stable version
            Latest  The latest available version
            2.1     The latest stable version for 2.1, e.g. 2.1.9 (channel version)
            2.1.*   The latest service release for 2.1, e.g. 2.1.10-servicing-12345
            2.1.8   This specific version
         */

        // Substituion values when "Latest" is passed as the version
        private static string LatestTargetFramework = "netcoreapp3.0";
        private static string LatestChannel = "3.0";

        // Substituion values when "Current" is passed as the version
        private static string CurrentTargetFramework = "netcoreapp2.2";
        private static string CurrentChannel = "2.2";

        private const string PerfViewVersion = "P2.0.26";

        private static readonly HttpClient _httpClient;
        private static readonly HttpClientHandler _httpClientHandler;
        private static readonly string _dotnetInstallShUrl = "https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.sh";
        private static readonly string _dotnetInstallPs1Url = "https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.ps1";
        private static readonly string _aspNetCoreDependenciesUrl = "https://raw.githubusercontent.com/aspnet/AspNetCore/{0}";
        private static readonly string _perfviewUrl = $"https://github.com/Microsoft/perfview/releases/download/{PerfViewVersion}/PerfView.exe";
        private static readonly string _currentAspNetApiUrl = "https://api.nuget.org/v3/registration3/microsoft.aspnetcore.app/index.json";
        private static readonly string _latestAspnetApiUrl = "https://dotnet.myget.org/F/aspnetcore-dev/api/v3/registration1/Microsoft.AspNetCore.App/index.json";
        private static readonly string _latestRuntimeApiUrl = "https://dotnet.myget.org/F/dotnet-core/api/v3/registration1/Microsoft.NETCore.App/index.json";
        private static readonly string _releaseMetadata = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json";
        private static readonly string _sdkVersionUrl = "https://dotnetcli.blob.core.windows.net/dotnet/Sdk/master/latest.version";
        private static readonly string _latestRuntimeUrl = "https://dotnetcli.blob.core.windows.net/dotnet/Runtime/master/latest.version";
        
        // Cached lists of SDKs and runtimes already installed
        private static readonly HashSet<string> _installedAspNetRuntimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _installedRuntimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _installedSdks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private const string _defaultUrl = "http://*:5001";
        private static readonly string _defaultHostname = Environment.MachineName.ToLowerInvariant();
        private static readonly string _perfviewPath;
        private static readonly string _dotnetInstallPath;

        private static readonly IRepository<ServerJob> _jobs = new InMemoryRepository<ServerJob>();
        private static readonly string _rootTempDir;
        private static bool _cleanup = true;
        private static Process perfCollectProcess;

        public static OperatingSystem OperatingSystem { get; }
        public static Hardware Hardware { get; private set; }
        public static string HardwareVersion { get; private set; }
        public static Dictionary<Database, string> ConnectionStrings = new Dictionary<Database, string>();
        public static TimeSpan DriverTimeout = TimeSpan.FromSeconds(10);
        public static TimeSpan BuildTimeout = TimeSpan.FromMinutes(30);

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

            // Use the same root temporary folder so we can clean it on restarts
            _rootTempDir = Path.Combine(Path.GetTempPath(), "BenchmarksServer");
            Directory.CreateDirectory(_rootTempDir);
            Log.WriteLine($"Cleaning temporary job files '{_rootTempDir}' ...");
            foreach (var tempFolder in Directory.GetDirectories(_rootTempDir))
            {
                try
                {
                    Log.WriteLine($"Attempting to deleting '{tempFolder}'");
                    File.Delete(tempFolder);
                }
                catch(Exception e)
                {
                    Log.WriteLine("Failed with error: " + e.Message);
                }
            }

            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClient = new HttpClient(_httpClientHandler);

            // Download PerfView
            if (OperatingSystem == OperatingSystem.Windows)
            {
                _perfviewPath = Path.Combine(Path.GetTempPath(), PerfViewVersion, Path.GetFileName(_perfviewUrl));

                // Ensure the folder already exists
                Directory.CreateDirectory(Path.GetDirectoryName(_perfviewPath));

                if (!File.Exists(_perfviewPath))
                {
                    Log.WriteLine($"Downloading PerfView to '{_perfviewPath}'");
                    DownloadFileAsync(_perfviewUrl, _perfviewPath, maxRetries: 5, timeout: 60).GetAwaiter().GetResult();
                }
                else
                {
                    Log.WriteLine($"Found PerfView locally at '{_perfviewPath}'");
                }
            }

            // Download dotnet-install at startup, once.
            _dotnetInstallPath = Path.Combine(_rootTempDir, Path.GetRandomFileName());

            // Ensure the folder already exists
            Directory.CreateDirectory(_dotnetInstallPath);

            var _dotnetInstallUrl = OperatingSystem == OperatingSystem.Windows
                ? _dotnetInstallPs1Url
                : _dotnetInstallShUrl
                ;

            var dotnetInstallFilename = Path.Combine(_dotnetInstallPath, Path.GetFileName(_dotnetInstallUrl));

            Log.WriteLine($"Downloading dotnet-install to '{dotnetInstallFilename}'");
            DownloadFileAsync(_dotnetInstallUrl, dotnetInstallFilename, maxRetries: 5, timeout: 60).GetAwaiter().GetResult();

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
            // Prevent unhandled exceptions in the benchmarked apps from displaying a popup that would block
            // the main process on Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SetErrorMode(ErrorModes.SEM_NONE);
            }

            var app = new CommandLineApplication()
            {
                Name = "BenchmarksServer",
                FullName = "ASP.NET Benchmark Server",
                Description = "REST APIs to run ASP.NET benchmark server",
                OptionsComparison = StringComparison.OrdinalIgnoreCase
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
                    .ConfigureLogging((hostingContext, logging) =>
                    {
                        logging.SetMinimumLevel(LogLevel.Error);
                        logging.AddConsole();
                    })
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
                // Create a temporary folder to store all installed dotnet runtimes/sdk
                dotnetHome = GetTempDir();

                Process process = null;
                string workingDirectory = null;
                Timer timer = null;
                var executionLock = new object();
                var disposed = false;
                var standardOutput = new StringBuilder();
                string benchmarksDir = null;
                var startMonitorTime = DateTime.UtcNow;

                string tempDir = null;
                string dockerImage = null;
                string dockerContainerId = null;

                while (!cancellationToken.IsCancellationRequested)
                {
                    ServerJob job = null;

                    // Find the first job that is not in Initializing state
                    foreach(var j in _jobs.GetAll())
                    {
                        if (j.State == ServerState.Initializing || j.State == ServerState.Stopped)
                        {
                            var now = DateTime.UtcNow;

                            if (now - j.LastDriverCommunicationUtc > DriverTimeout)
                            {
                                // The job needs to be deleted
                                Log.WriteLine($"Driver didn't communicate for {DriverTimeout}. Halting job.");
                                j.State = ServerState.Deleting;
                            }
                            else
                            {
                                // Initializing jobs are skipped
                                continue;
                            }
                        }

                        job = j;
                        break;
                    }

                    if (job != null)
                    {
                        if (job.State == ServerState.Failed)
                        {
                            var now = DateTime.UtcNow;

                            // Clean the job in case the driver is not running
                            if (now - job.LastDriverCommunicationUtc > DriverTimeout)
                            {
                                Log.WriteLine($"Driver didn't communicate for {DriverTimeout}. Halting job.");
                                job.State = ServerState.Deleting;
                            }
                        }
                        else if (job.State == ServerState.Waiting)
                        {
                            // TODO: Race condition if DELETE is called during this code
                            try
                            {
                                if (OperatingSystem == OperatingSystem.Linux &&
                                    (job.WebHost == WebHost.IISInProcess ||
                                    job.WebHost == WebHost.IISOutOfProcess)
                                    )
                                {
                                    Log.WriteLine($"Skipping job '{job.Id}' with scenario '{job.Scenario}'.");
                                    Log.WriteLine($"'{job.WebHost}' is not supported on this platform.");
                                    job.State = ServerState.NotSupported;
                                    continue;
                                }

                                Log.WriteLine($"Starting job '{job.Id}' with scenario '{job.Scenario}'");
                                job.State = ServerState.Starting;

                                standardOutput.Clear();
                                startMonitorTime = DateTime.UtcNow;

                                Debug.Assert(tempDir == null);
                                tempDir = GetTempDir();
                                workingDirectory = null;
                                dockerImage = null;

                                if (job.Source.DockerFile != null)
                                {
                                    try
                                    {
                                        var buildStart = DateTime.UtcNow;
                                        var cts = new CancellationTokenSource();
                                        var buildAndRunTask = Task.Run(() => DockerBuildAndRun(tempDir, job, hostname, standardOutput, cancellationToken: cts.Token));

                                        while (true)
                                        {
                                            if (buildAndRunTask.IsCompleted)
                                            {
                                                (dockerContainerId, dockerImage, workingDirectory) = buildAndRunTask.Result;
                                                break;
                                            }

                                            // Cancel the build if the driver timed out
                                            if (DateTime.UtcNow - job.LastDriverCommunicationUtc > DriverTimeout)
                                            {
                                                Log.WriteLine($"Driver didn't communicate for {DriverTimeout}. Halting build.");
                                                cts.Cancel();
                                                await buildAndRunTask;

                                                job.State = ServerState.Failed;
                                                break;
                                            }

                                            // Cancel the build if it's taking too long
                                            if (DateTime.UtcNow - buildStart > BuildTimeout)
                                            {
                                                Log.WriteLine($"Build is taking too long. Halting build.");
                                                cts.Cancel();
                                                await buildAndRunTask;

                                                job.Error = "Build is taking too long. Halting build.";
                                                job.State = ServerState.Failed;
                                                break;
                                            }

                                            await Task.Delay(1000);
                                        }
                                    }
                                    catch(Exception e)
                                    {
                                        workingDirectory = null;
                                        Log.WriteLine($"Job failed with DockerBuildAndRun: " + e.Message);
                                        job.State = ServerState.Failed;
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        // returns the application directory and the dotnet directory to use
                                        benchmarksDir = await CloneRestoreAndBuild(tempDir, job, dotnetHome);
                                    }
                                    finally
                                    {
                                        if (benchmarksDir != null)
                                        {
                                            Debug.Assert(process == null);
                                            process = await StartProcess(hostname, Path.Combine(tempDir, benchmarksDir), job, dotnetHome, standardOutput);

                                            job.ProcessId = process.Id;

                                            workingDirectory = process.StartInfo.WorkingDirectory;
                                        }
                                        else
                                        {
                                            workingDirectory = null;
                                            Log.WriteLine($"Job failed with CloneRestoreAndBuild");
                                            job.State = ServerState.Failed;
                                        }
                                    }
                                }

                                startMonitorTime = DateTime.UtcNow;
                                var lastMonitorTime = startMonitorTime;
                                var oldCPUTime = TimeSpan.Zero;

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

                                        // Clean the job in case the driver is not running
                                        if (now - job.LastDriverCommunicationUtc > DriverTimeout)
                                        {
                                            Log.WriteLine($"Driver didn't communicate for {DriverTimeout}. Halting job.");
                                            job.State = ServerState.Deleting;
                                        }

                                        if (process != null)
                                        {
                                            if (process.HasExited)
                                            {
                                                if (process.ExitCode != 0)
                                                {
                                                    Log.WriteLine($"Job failed");

                                                    job.Error = "Job failed at runtime\n" + standardOutput.ToString();
                                                    job.State = ServerState.Failed;
                                                }
                                                else
                                                {
                                                    job.State = ServerState.Stopped;
                                                }
                                            }
                                            else
                                            {
                                                // TODO: Accessing the TotalProcessorTime on OSX throws so just leave it as 0 for now
                                                // We need to dig into this
                                                var newCPUTime = OperatingSystem == OperatingSystem.OSX ? TimeSpan.Zero : process.TotalProcessorTime;
                                                var elapsed = now.Subtract(lastMonitorTime).TotalMilliseconds;
                                                var cpu = Math.Round((newCPUTime - oldCPUTime).TotalMilliseconds / (Environment.ProcessorCount * elapsed) * 100);
                                                lastMonitorTime = now;

                                                process.Refresh();

                                                // Ignore first measure
                                                if (oldCPUTime != TimeSpan.Zero)
                                                {
                                                    job.AddServerCounter(new ServerCounter
                                                    {
                                                        Elapsed = now - startMonitorTime,
                                                        WorkingSet = process.WorkingSet64,
                                                        CpuPercentage = cpu
                                                    });
                                                }

                                                oldCPUTime = newCPUTime;
                                            }
                                        }
                                        else if (!String.IsNullOrEmpty(dockerImage))
                                        {
                                            var output = new StringBuilder();

                                            // Check the container is still running
                                            ProcessUtil.Run("docker", "inspect -f {{.State.Running}} " + dockerContainerId,
                                                outputDataReceived: d => output.AppendLine(d),
                                                log: false);

                                            if (output.ToString().Contains("false"))
                                            {
                                                job.State = ServerState.Stopping;
                                            }
                                            else
                                            {
                                                // Get docker stats
                                                output.Clear();
                                                var result = ProcessUtil.Run("docker", "container stats --no-stream --format \"{{.CPUPerc}}-{{.MemUsage}}\" " + dockerContainerId,
                                                    outputDataReceived: d => output.AppendLine(d),
                                                    log: false);

                                                var data = output.ToString().Trim().Split('-');

                                                // Format is {value}%
                                                var cpuPercentRaw = data[0];

                                                // Format is {used}M/GiB/{total}M/GiB
                                                var workingSetRaw = data[1];
                                                var usedMemoryRaw = workingSetRaw.Split('/')[0].Trim();
                                                var cpu = Math.Round(double.Parse(cpuPercentRaw.Trim('%')) / Environment.ProcessorCount);

                                                // MiB, GiB, B ?
                                                var factor = 1;
                                                double memory;

                                                if (usedMemoryRaw.EndsWith("GiB"))
                                                {
                                                    factor = 1024 * 1024 * 1024;
                                                    memory = double.Parse(usedMemoryRaw.Substring(0, usedMemoryRaw.Length - 3));
                                                }
                                                else if (usedMemoryRaw.EndsWith("MiB"))
                                                {
                                                    factor = 1024 * 1024;
                                                    memory = double.Parse(usedMemoryRaw.Substring(0, usedMemoryRaw.Length - 3));
                                                }
                                                else
                                                {
                                                    memory = double.Parse(usedMemoryRaw.Substring(0, usedMemoryRaw.Length - 1));
                                                }

                                                var workingSet = (long)(memory * factor);

                                                job.AddServerCounter(new ServerCounter
                                                {
                                                    Elapsed = now - startMonitorTime,
                                                    WorkingSet = workingSet,
                                                    CpuPercentage = cpu > 100 ? 0 : cpu
                                                });
                                            }
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

                                disposed = false;
                            }
                            catch (Exception e)
                            {
                                Log.WriteLine($"Error starting job '{job.Id}': {e}");
                                job.State = ServerState.Failed;
                                continue;
                            }
                        }
                        else if (job.State == ServerState.Stopping)
                        {
                            Log.WriteLine($"Stopping job '{job.Id}' with scenario '{job.Scenario}'");

                            await StopJobAsync();
                        }
                        else if (job.State == ServerState.Stopped || job.State == ServerState.Failed)
                        {
                            Log.WriteLine($"Job '{job.Id}' is stopped, waiting for the driver to delete it");
                        }
                        else if (job.State == ServerState.Deleting)
                        {
                            Log.WriteLine($"Deleting job '{job.Id}' with scenario '{job.Scenario}'");

                            await DeleteJobAsync();
                        }
                        else if (job.State == ServerState.TraceCollecting)
                        {
                            // Stop perfview
                            if (job.Collect)
                            {
                                if (OperatingSystem == OperatingSystem.Windows)
                                {
                                    RunPerfview("stop /AcceptEula /NoNGenRundown /NoView", benchmarksDir);
                                }
                                else if (OperatingSystem == OperatingSystem.Linux)
                                {
                                    await StopPerfcollectAsync(perfCollectProcess);
                                }

                                Log.WriteLine("Trace collected");
                                job.State = ServerState.TraceCollected;
                            }

                        }
                        else if (job.State == ServerState.Starting)
                        {
                            if (DateTime.UtcNow - startMonitorTime > DriverTimeout)
                            {
                                Log.WriteLine($"Job didn't start during the expected delay");
                                job.State = ServerState.Stopping;
                            }
                        }

                        async Task StopJobAsync()
                        {
                            lock (executionLock)
                            {
                                disposed = true;

                                timer?.Dispose();
                                timer = null;
                            }

                            if (process != null && !process.HasExited)
                            {
                                var processId = process.Id;

                                if (job.Collect)
                                {
                                    // Abort all perfview processes
                                    if (OperatingSystem == OperatingSystem.Windows)
                                    {
                                        var perfViewProcess = RunPerfview("abort", Path.GetPathRoot(_perfviewPath));
                                    }
                                    else if (OperatingSystem == OperatingSystem.Linux)
                                    {
                                        // TODO: Stop perfcollect
                                    }
                                }

                                if (OperatingSystem == OperatingSystem.Linux)
                                {
                                    Mono.Unix.Native.Syscall.kill(process.Id, Mono.Unix.Native.Signum.SIGINT);

                                    // Tentatively invoke SIGINT
                                    var waitForShutdownDelay = Task.Delay(TimeSpan.FromSeconds(5));
                                    while (!process.HasExited && !waitForShutdownDelay.IsCompletedSuccessfully)
                                    {
                                        await Task.Delay(200);
                                    }
                                }

                                if (!process.HasExited)
                                {
                                    if (OperatingSystem == OperatingSystem.Linux)
                                    {
                                        Log.WriteLine($"SIGINT was not handled, checking /shutdown endpoint ...");
                                    }

                                    try
                                    {
                                        // Tentatively invoke the shutdown endpoint on the client application
                                        var response = await _httpClient.GetAsync(new Uri(new Uri(job.Url), "/shutdown"));

                                        // Shutdown invoked successfully, wait for the application to stop by itself
                                        if (response.StatusCode == System.Net.HttpStatusCode.OK)
                                        {
                                            var epoch = DateTime.UtcNow;

                                            do
                                            {
                                                Log.WriteLine($"Shutdown successfully invoked, waiting for graceful shutdown ...");
                                                await Task.Delay(1000);

                                            } while (!process.HasExited && (DateTime.UtcNow - epoch < TimeSpan.FromSeconds(5)));
                                        }
                                    }
                                    catch
                                    {
                                        Log.WriteLine($"/shutdown endpoint failed...");
                                    }
                                }

                                if (!process.HasExited)
                                {
                                    Log.WriteLine($"Forcing process to stop ...");
                                    process.CloseMainWindow();

                                    if (!process.HasExited)
                                    {
                                        process.Kill();
                                    }

                                    process.Dispose();

                                    do
                                    {
                                        Log.WriteLine($"Waiting for process {processId} to stop ...");

                                        await Task.Delay(1000);

                                        try
                                        {
                                            process = Process.GetProcessById(processId);
                                            process.Refresh();
                                        }
                                        catch
                                        {
                                            process = null;
                                        }

                                    } while (process != null && !process.HasExited);
                                }

                                Log.WriteLine($"Process has stopped");

                                job.State = ServerState.Stopped;

                                process = null;
                            }
                            else if (!String.IsNullOrEmpty(dockerImage))
                            {
                                DockerCleanUp(dockerContainerId, dockerImage, job, standardOutput);
                            }

                            // Running AfterScript
                            if (!String.IsNullOrEmpty(job.AfterScript))
                            {
                                var segments = job.AfterScript.Split(' ', 2);
                                var processResult = ProcessUtil.Run(segments[0], segments.Length > 1 ? segments[1] : "", log: true, workingDirectory: workingDirectory);
                                standardOutput.AppendLine(processResult.StandardOutput);
                            }

                            job.Output = standardOutput.ToString();
                            
                            Log.WriteLine($"Process stopped ({job.State})");
                        }

                        async Task DeleteJobAsync()
                        {
                            await StopJobAsync();

                            if (_cleanup && !job.NoClean && tempDir != null)
                            {
                                DeleteDir(tempDir);
                            }

                            tempDir = null;

                            _jobs.Remove(job.Id);
                        }
                    }

                    await Task.Delay(1000);
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

        private static Process RunPerfcollect(string arguments, string workingDirectory)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Log.WriteLine($"PerfCollect is only supported on Linux");
                return null;
            }

            var process = new Process()
            {
                StartInfo = {
                    FileName = "perfcollect",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                }
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (e != null && e.Data != null)
                {
                    Log.WriteLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();

            Log.WriteLine($"Perfcollect started [{process.Id}]");

            return process;
        }

        private static async Task StopPerfcollectAsync(Process perfCollectProcess)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Log.WriteLine($"PerfCollect is only supported on Linux");
                return;
            }

            if (perfCollectProcess.HasExited)
            {
                Log.WriteLine($"PerfCollect is not running");
                return;
            }

            var processId = perfCollectProcess.Id;

            Log.WriteLine($"Stopping PerfCollect");

            Mono.Unix.Native.Syscall.kill(processId, Mono.Unix.Native.Signum.SIGINT);

            // Max delay for perfcollect to stop
            var delay = Task.Delay(30000);

            while(!perfCollectProcess.HasExited && !delay.IsCompletedSuccessfully)
            {
                await Task.Delay(1000);
            }

            if (!perfCollectProcess.HasExited)
            {
                Log.WriteLine($"Forcing process to stop ...");
                perfCollectProcess.CloseMainWindow();

                if (!perfCollectProcess.HasExited)
                {
                    perfCollectProcess.Kill();
                }

                perfCollectProcess.Dispose();

                do
                {
                    Log.WriteLine($"Waiting for process {processId} to stop ...");

                    await Task.Delay(1000);

                    try
                    {
                        perfCollectProcess = Process.GetProcessById(processId);
                        perfCollectProcess.Refresh();
                    }
                    catch
                    {
                        perfCollectProcess = null;
                    }

                } while (perfCollectProcess != null && !perfCollectProcess.HasExited);
            }
            Log.WriteLine($"Process has stopped");

            perfCollectProcess = null;

        }

        private static async Task<(string containerId, string imageName, string workingDirectory)> DockerBuildAndRun(string path, ServerJob job, string hostname, StringBuilder standardOutput, CancellationToken cancellationToken = default(CancellationToken))
        {
            var source = job.Source;
            string srcDir;

            // Docker image names must be lowercase
            var imageName = $"benchmarks_{source.DockerImageName}".ToLowerInvariant();

            if (source.SourceCode != null)
            {
                srcDir = Path.Combine(path, "src");
                Log.WriteLine($"Extracting source code to {srcDir}");

                ZipFile.ExtractToDirectory(job.Source.SourceCode.TempFilename, srcDir);

                File.Delete(job.Source.SourceCode.TempFilename);
            }
            else
            {
                srcDir = Path.Combine(path, Git.Clone(path, source.Repository, shallow: true, branch: source.BranchOrCommit));
            }

            var workingDirectory = Path.Combine(srcDir, source.DockerContextDirectory);
            
            // Running BeforeScript
            if (!String.IsNullOrEmpty(job.BeforeScript))
            {
                var segments = job.BeforeScript.Split(' ', 2);
                var processResult = ProcessUtil.Run(segments[0], segments.Length > 1 ? segments[1] : "", workingDirectory: workingDirectory, log: true);
                standardOutput.AppendLine(processResult.StandardOutput);
            }

            ProcessUtil.Run("docker", $"build --pull -t {imageName} -f {source.DockerFile} {workingDirectory}", workingDirectory: srcDir, timeout: BuildTimeout, cancellationToken: cancellationToken, log: true);

            if (cancellationToken.IsCancellationRequested)
            {
                return (null, null, null);
            }

            // Only run on the host network on linux
            var useHostNetworking = OperatingSystem == OperatingSystem.Linux;

            var environmentArguments = "";

            foreach (var env in job.EnvironmentVariables)
            {
                environmentArguments += $"--env {env.Key}={env.Value} ";
            }

            var command = useHostNetworking ? $"run -d {environmentArguments} {job.Arguments} --network host {imageName}" :
                                              $"run -d {environmentArguments} {job.Arguments} -p {job.Port}:{job.Port} {imageName}";

            var result = ProcessUtil.Run("docker", $"{command} ", throwOnError: false);

            var containerId = result.StandardOutput.Trim();
            job.Url = ComputeServerUrl(hostname, job);

            var stopwatch = new Stopwatch();

            if (!String.IsNullOrEmpty(job.ReadyStateText))
            {
                Log.WriteLine($"Waiting for startup signal: '{job.ReadyStateText}'...");

                var process = new Process()
                {
                    StartInfo = {
                    FileName = "docker",
                    Arguments = $"logs -f {containerId}",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
                    EnableRaisingEvents = true
                };

                process.OutputDataReceived += (_, e) =>
                {
                    if (e != null && e.Data != null)
                    {
                        Log.WriteLine(e.Data);
                        standardOutput.AppendLine(e.Data);

                        if (job.State == ServerState.Starting && e.Data.IndexOf(job.ReadyStateText, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Log.WriteLine($"Application is now running...");
                            MarkAsRunning(hostname, job, stopwatch);
                        }
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
            }
            else
            {
                Log.WriteLine($"Waiting for application to startup...");

                // Wait until the service is reachable to avoid races where the container started but isn't
                // listening yet. If it keeps failing we ignore it. If the port is unreachable then clients
                // will fail to connect and the job will be cleaned up properly
                if (await WaitToListen(job, hostname, 30))
                {
                    Log.WriteLine($"Application is now running...");
                }
                else
                {
                    Log.WriteLine($"Application MAY be running, continuing...");
                }

                MarkAsRunning(hostname, job, stopwatch);
            }

            return (containerId, imageName, workingDirectory);
        }

        private static async Task<bool> WaitToListen(ServerJob job, string hostname, int maxRetries = 5)
        {
            for (var i = 1; i <= maxRetries; ++i)
            {
                try
                {
                    Log.WriteLine($"Trying to access server, attempt #{i} ...");
                    using (var tcpClient = new TcpClient())
                    {
                        var connectTask = tcpClient.ConnectAsync(hostname, job.Port);
                        await Task.WhenAny(connectTask, Task.Delay(1000));
                        if (connectTask.IsCompleted)
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    await Task.Delay(300);
                }
            }

            return false;
        }

        private static void DockerCleanUp(string containerId, string imageName, ServerJob job, StringBuilder standardOutput)
        {
            var state = ProcessUtil.Run("docker", "inspect -f {{.State.Running}} " + containerId).StandardOutput;

            // container is already stopped
            if (state.Contains("false"))
            {
                if (ProcessUtil.Run("docker", "inspect -f {{.State.ExitCode}} " + containerId).StandardOutput.Trim() != "0")
                {
                    Log.WriteLine("Job failed");
                    job.Error = ProcessUtil.Run("docker", "logs " + containerId).StandardError;
                    job.State = ServerState.Failed;
                }
                else
                {
                    job.State = ServerState.Stopped;
                }
            }
            else
            {
                ProcessUtil.Run("docker", $"stop {containerId}");

                job.State = ServerState.Stopped;
            }

            ProcessUtil.Run("docker", $"rmi --force {imageName}");
        }

        private static async Task<string> CloneRestoreAndBuild(string path, ServerJob job, string dotnetHome)
        {
            // Clone
            string benchmarkedDir = null;

            if (job.Source.SourceCode != null)
            {
                benchmarkedDir = "src";

                var src = Path.Combine(path, benchmarkedDir);
                Log.WriteLine($"Extracting source code to {src}");

                ZipFile.ExtractToDirectory(job.Source.SourceCode.TempFilename, src);

                File.Delete(job.Source.SourceCode.TempFilename);
            }
            else
            {
                // It's possible that the user specified a custom branch/commit for the benchmarks repo,
                // so we need to add that to the set of sources to restore if it's not already there.
                //
                // Note that this is also going to de-dupe the repos if the same one was specified twice at
                // the command-line (last first to support overrides).
                var repos = new HashSet<Source>(SourceRepoComparer.Instance);

                repos.Add(job.Source);

                foreach (var source in repos)
                {
                    var dir = Git.Clone(path, source.Repository, shallow: true, branch: source.BranchOrCommit);
                    if (SourceRepoComparer.Instance.Equals(source, job.Source))
                    {
                        benchmarkedDir = dir;
                    }

                    Git.InitSubModules(Path.Combine(path, dir));
                }
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

            Log.WriteLine("Downloading build tools");

            // Install latest SDK and runtime
            // * Use custom install dir to avoid changing the default install, which is impossible if other processes
            //   are already using it.
            var buildToolsPath = Path.Combine(path, "buildtools");
            if (!Directory.Exists(buildToolsPath))
            {
                Directory.CreateDirectory(buildToolsPath);
            }

            Log.WriteLine($"Installing dotnet runtimes and sdk");

            // Computes the location of the benchmarked app
            var benchmarkedApp = Path.Combine(path, benchmarkedDir, Path.GetDirectoryName(job.Source.Project));

            // Define which Runtime and SDK will be installed.

            string targetFramework;
            string runtimeVersion;
            string aspNetCoreVersion;
            string channel;

            // Default targetFramework (Latest)
            targetFramework = LatestTargetFramework;

            if (String.Equals(job.RuntimeVersion, "Current", StringComparison.OrdinalIgnoreCase))
            {
                runtimeVersion = await GetRuntimeChannelVersion(CurrentChannel);
                targetFramework = CurrentTargetFramework;
                channel = CurrentChannel;
            }
            else if(String.Equals(job.RuntimeVersion, "Latest", StringComparison.OrdinalIgnoreCase))
            {
                // Get the version that is defined by the ASP.NET repository
                // Note: to use the latest version available, use a value like 3.0.*
                runtimeVersion = await GetAspNetRuntimeVersion(buildToolsPath, LatestTargetFramework);
                channel = LatestChannel;
            }
            else
            {
                // Custom version
                runtimeVersion = job.RuntimeVersion;
                
                if (runtimeVersion.EndsWith("*", StringComparison.Ordinal))
                {
                    // Prefixed version
                    // Detect the latest available version with this prefix

                    channel = String.Join(".", runtimeVersion.Split('.').Take(2));

                    if (channel == "3.0")
                    {
                        runtimeVersion = await ParseLatestVersionFile(_latestRuntimeUrl);
                    }
                    else
                    {
                        runtimeVersion = await GetLatestPackageVersion(_latestRuntimeApiUrl, runtimeVersion.TrimEnd('*'));
                    }
                }
                else if (runtimeVersion.Split('.').Length == 2)
                {
                    // Channel version with a prefix, e.g. 2.1
                    channel = runtimeVersion;
                    runtimeVersion = await GetRuntimeChannelVersion(runtimeVersion);
                }
                else
                {
                    // Specific version
                    channel = String.Join(".", runtimeVersion.Split('.').Take(2));
                }

                if (runtimeVersion.StartsWith("2.1"))
                {
                    targetFramework = "netcoreapp2.1";
                }
                else if (runtimeVersion.StartsWith("2.2"))
                {
                    targetFramework = "netcoreapp2.2";
                }
                else if (runtimeVersion.StartsWith("3.0"))
                {
                    targetFramework = "netcoreapp3.0";
                }
            }

            // If a specific framework is set, use it instead of the detected one
            if (!String.IsNullOrEmpty(job.Framework))
            {
                targetFramework = job.Framework;
            }

            string sdkVersion;

            if (runtimeVersion.StartsWith("3.0"))
            {
                sdkVersion = await ParseLatestVersionFile(_sdkVersionUrl);
                Log.WriteLine($"Detecting latest SDK version: {sdkVersion}");
            }
            else
            {
                sdkVersion = await GetReleasedSdkChannelVersion(channel);
            }

            if (!String.IsNullOrEmpty(job.SdkVersion))
            {
                if (String.Equals(sdkVersion, "stable", StringComparison.OrdinalIgnoreCase))
                {
                    sdkVersion = await GetReleasedSdkChannelVersion(channel);
                }

                sdkVersion = job.SdkVersion;
            }

            var globalJson = "{ \"sdk\": { \"version\": \"" + sdkVersion + "\" } }";
            Log.WriteLine($"Writing global.json with content: {globalJson}");
            File.WriteAllText(Path.Combine(benchmarkedApp, "global.json"), globalJson);

            // Define which ASP.NET Core packages version to use

            if (String.Equals(job.AspNetCoreVersion, "Current", StringComparison.OrdinalIgnoreCase))
            {
                aspNetCoreVersion = await GetLatestPackageVersion(_currentAspNetApiUrl, CurrentChannel + ".");
            }
            else if (String.Equals(job.AspNetCoreVersion, "Latest", StringComparison.OrdinalIgnoreCase))
            {
                aspNetCoreVersion = await GetLatestPackageVersion(_latestAspnetApiUrl, LatestChannel + ".");
            }
            else
            {
                // Custom version
                aspNetCoreVersion = job.AspNetCoreVersion;

                if (aspNetCoreVersion.EndsWith("*", StringComparison.Ordinal))
                {
                    // Prefixed version
                    // Detect the latest available version with this prefix

                    aspNetCoreVersion = await GetLatestPackageVersion(_latestAspnetApiUrl, aspNetCoreVersion.TrimEnd('*'));
                }
                else if (aspNetCoreVersion.Split('.').Length == 2)
                {
                    // Channel version with a prefix, e.g. 2.1
                    aspNetCoreVersion = await GetLatestPackageVersion(_currentAspNetApiUrl, aspNetCoreVersion + ".");
                }
            }

            Log.WriteLine($"Detected ASP.NET version: {aspNetCoreVersion}");

            var installAspNetSharedFramework = job.UseRuntimeStore || aspNetCoreVersion.StartsWith("3.0");

            var dotnetInstallStep = "";

            try
            {
                if (OperatingSystem == OperatingSystem.Windows)
                {
                    if (!_installedSdks.Contains(sdkVersion))
                    {
                        dotnetInstallStep = $"SDK version '{sdkVersion}'";

                        // Install latest SDK version (and associated runtime)
                        ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted .\\dotnet-install.ps1 -Version {sdkVersion} -NoPath -SkipNonVersionedFiles -InstallDir {dotnetHome}", 
                        log:true,
                        workingDirectory: _dotnetInstallPath,
                        environmentVariables: env));

                        _installedSdks.Add(sdkVersion);
                    }

                    if (!_installedRuntimes.Contains(runtimeVersion))
                    {
                        dotnetInstallStep = $"Core CLR version '{runtimeVersion}'";

                        // Install runtime required for this scenario
                        ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted .\\dotnet-install.ps1 -Version {runtimeVersion} -Runtime dotnet -NoPath -SkipNonVersionedFiles -InstallDir {dotnetHome}",
                        log: true, 
                        workingDirectory: _dotnetInstallPath,
                        environmentVariables: env));

                        _installedRuntimes.Add(runtimeVersion);
                    }

                    // The aspnet core runtime is only available for >= 2.1, in 2.0 the dlls are contained in the runtime store
                    if (installAspNetSharedFramework && !_installedAspNetRuntimes.Contains(aspNetCoreVersion))
                    {
                        dotnetInstallStep = $"ASP.NET version '{aspNetCoreVersion}'";

                        // Install aspnet runtime required for this scenario
                        ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted .\\dotnet-install.ps1 -Version {aspNetCoreVersion} -Runtime aspnetcore -NoPath -SkipNonVersionedFiles -InstallDir {dotnetHome}",
                        log: true,
                        workingDirectory: _dotnetInstallPath,
                        environmentVariables: env));

                        _installedAspNetRuntimes.Add(aspNetCoreVersion);
                    }
                }
                else
                {
                    if (!_installedSdks.Contains(sdkVersion))
                    {
                        dotnetInstallStep = $"SDK version '{sdkVersion}'";
                        
                        // Install latest SDK version (and associated runtime)
                        ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --version {sdkVersion} --no-path --skip-non-versioned-files --install-dir {dotnetHome}",
                        log: true,
                        workingDirectory: _dotnetInstallPath,
                        environmentVariables: env));
                        _installedSdks.Add(sdkVersion);
                    }

                    if (!_installedRuntimes.Contains(runtimeVersion))
                    {
                        dotnetInstallStep = $"Core CLR version '{runtimeVersion}'";

                        // Install required runtime 
                        ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --version {runtimeVersion} --runtime dotnet --no-path --skip-non-versioned-files --install-dir {dotnetHome}",
                        log: true,
                        workingDirectory: _dotnetInstallPath,
                        environmentVariables: env));

                        _installedRuntimes.Add(runtimeVersion);
                    }

                    // The aspnet core runtime is only available for >= 2.1, in 2.0 the dlls are contained in the runtime store
                    if (installAspNetSharedFramework && !_installedAspNetRuntimes.Contains(aspNetCoreVersion))
                    {
                        dotnetInstallStep = $"ASP.NET version '{aspNetCoreVersion}'";

                        // Install required runtime 
                        ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --version {aspNetCoreVersion} --runtime aspnetcore --no-path --skip-non-versioned-files --install-dir {dotnetHome}",
                        log: true,
                        workingDirectory: _dotnetInstallPath,
                        environmentVariables: env));

                        _installedAspNetRuntimes.Add(aspNetCoreVersion);
                    }
                }
            }
            catch
            {
                job.Error = $"dotnet-install could not install this runtime: {dotnetInstallStep}";

                return null;
            }

            var dotnetDir = dotnetHome;

            // Updating ServerJob to reflect actual versions used
            job.AspNetCoreVersion = aspNetCoreVersion;
            job.RuntimeVersion = runtimeVersion;
            job.SdkVersion = sdkVersion;

            // Build and Restore
            var dotnetExecutable = GetDotNetExecutable(dotnetDir);

            var buildParameters = $"/p:BenchmarksAspNetCoreVersion={aspNetCoreVersion} " +
                $"/p:MicrosoftAspNetCoreAllPackageVersion={aspNetCoreVersion} " +
                $"/p:MicrosoftAspNetCoreAppPackageVersion={aspNetCoreVersion} " +
                $"/p:BenchmarksNETStandardImplicitPackageVersion={aspNetCoreVersion} " +
                $"/p:BenchmarksNETCoreAppImplicitPackageVersion={aspNetCoreVersion} " +
                $"/p:BenchmarksRuntimeFrameworkVersion={runtimeVersion} " +
                $"/p:BenchmarksTargetFramework={targetFramework} " +
                $"/p:MicrosoftNETCoreAppPackageVersion={runtimeVersion} " +
                $"/p:NETCoreAppMaximumVersion=99.9 "; // Force the SDK to accept the TFM even if it's an unknown one. For instance using SDK 2.1 to build a netcoreapp2.2 TFM.

            if (targetFramework == "netcoreapp2.1")
            {
                buildParameters += $"/p:MicrosoftNETCoreApp21PackageVersion={runtimeVersion} ";
                if (!job.UseRuntimeStore)
                {
                    buildParameters += $"/p:MicrosoftNETPlatformLibrary=Microsoft.NETCore.App ";
                }
            }
            else if (targetFramework == "netcoreapp2.2")
            {
                buildParameters += $"/p:MicrosoftNETCoreApp22PackageVersion={runtimeVersion} ";
                if (!job.UseRuntimeStore)
                {
                    buildParameters += $"/p:MicrosoftNETPlatformLibrary=Microsoft.NETCore.App ";
                }
            }
            else if (targetFramework == "netcoreapp3.0")
            {
                buildParameters += $"/p:MicrosoftNETCoreApp30PackageVersion={runtimeVersion} ";
                if (!job.UseRuntimeStore)
                {
                    buildParameters += $"/p:MicrosoftNETPlatformLibrary=Microsoft.NETCore.App ";
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported framework: {targetFramework}");
            }

            // Apply custom build arguments sent from the driver
            foreach (var argument in job.BuildArguments)
            {
                buildParameters += $"{argument} ";
            }

            // Specify tfm in case the project targets multiple one
            buildParameters += $"--framework {targetFramework} ";

            if (job.SelfContained)
            {
                buildParameters += $"--self-contained ";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    buildParameters += "-r win-x64 ";
                }
                else
                {
                    buildParameters += "-r linux-x64 ";
                }
            }

            var outputFolder = Path.Combine(benchmarkedApp, "published");

            var startPublish = DateTime.UtcNow;

            var arguments = $"publish -c Release -o {outputFolder} {buildParameters}";

            Log.WriteLine($"Publishing application in {outputFolder} with: \n {arguments}");

            var buildResults = ProcessUtil.Run(dotnetExecutable, arguments,
                workingDirectory: benchmarkedApp,
                environmentVariables: env,
                throwOnError: false);

            if (buildResults.ExitCode != 0)
            {
                job.Error = $"Command dotnet {arguments} returned exit code {buildResults.ExitCode} \n" +
                    buildResults.StandardOutput + "\n" +
                    buildResults.StandardError;

                return null;
            }

            Log.WriteLine($"Application published successfully in {DateTime.UtcNow - startPublish}");

            // Copy crossgen in the app folder
            if (job.Collect && OperatingSystem == OperatingSystem.Linux)
            {
                Log.WriteLine("Copying crossgen to application folder");

                // Downloading corresponding package

                var runtimePath = Path.Combine(_rootTempDir, "RuntimePackages", $"runtime.linux-x64.Microsoft.NETCore.App.{runtimeVersion}.nupkg");

                // Ensure the folder already exists
                Directory.CreateDirectory(Path.GetDirectoryName(runtimePath));

                if (!File.Exists(runtimePath))
                {
                    Log.WriteLine($"Downloading runtime package");
                    await DownloadFileAsync($"https://dotnet.myget.org/F/dotnet-core/api/v2/package/runtime.linux-x64.Microsoft.NETCore.App/{runtimeVersion}", runtimePath, maxRetries: 5, timeout: 60);
                }
                else
                {
                    Log.WriteLine($"Found runtime package at '{runtimePath}'");
                }

                using (var archive = ZipFile.OpenRead(runtimePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith("/crossgen", StringComparison.OrdinalIgnoreCase))
                        {
                            var crossgenFolder = job.SelfContained
                                ? outputFolder
                                : Path.Combine(dotnetDir, "shared", "Microsoft.NETCore.App", runtimeVersion)
                                ;

                            var crossgenFilename = Path.Combine(crossgenFolder, "crossgen");

                            if (!File.Exists(crossgenFilename))
                            {
                                // Ensure the target folder is created
                                Directory.CreateDirectory(Path.GetDirectoryName(crossgenFilename));

                                entry.ExtractToFile(crossgenFilename);
                                Log.WriteLine($"Copied crossgen to {crossgenFolder}");
                            }

                            break;
                        }
                    }
                }
            }

            // Copy all output attachments
            foreach (var attachment in job.Attachments)
            {
                var filename = Path.Combine(outputFolder, attachment.Filename.Replace("\\", "/"));

                Log.WriteLine($"Creating output file: {filename}");

                if (File.Exists(filename))
                {
                    File.Delete(filename);
                }

                File.Copy(attachment.TempFilename, filename);
                File.Delete(attachment.TempFilename);
            }

            return benchmarkedDir;
        }

        /// <summary>
        /// Retrieves the runtime version used on ASP.NET Coherence builds
        /// </summary>
        private static async Task<string> GetAspNetRuntimeVersion(string buildToolsPath, string targetFramework)
        {
            // Maps a TFM to the github branch of several repositories
            var TfmToBranches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"netcoreapp2.1", "release/2.1/build/dependencies.props"},
                {"netcoreapp2.2", "release/2.2/build/dependencies.props"},
                {"netcoreapp3.0", "master/eng/Versions.props"}
            };

            var aspNetCoreDependenciesPath = Path.Combine(buildToolsPath, Path.GetFileName(_aspNetCoreDependenciesUrl));
            await DownloadFileAsync(String.Format(_aspNetCoreDependenciesUrl, TfmToBranches[targetFramework]), aspNetCoreDependenciesPath, maxRetries: 5, timeout: 10);
            var latestRuntimeVersion = XDocument.Load(aspNetCoreDependenciesPath).Root
                .Element("PropertyGroup")
                .Element("MicrosoftNETCoreAppPackageVersion")
                .Value;

            Log.WriteLine($"Detecting AspNetCore repository runtime version: {latestRuntimeVersion}");
            return latestRuntimeVersion;
        }
        
        /// <summary>
        /// Retrieves the current runtime version for a channel
        /// </summary>
        private static async Task<string> GetRuntimeChannelVersion(string channel)
        {
            var content = await DownloadContentAsync(_releaseMetadata);

            var index = JObject.Parse(content);
            var channelDotnetRuntime = index.SelectToken($"$.releases-index[?(@.channel-version == '{channel}')].latest-runtime").ToString();

            Log.WriteLine($"Detecting current runtime version for channel {channel}: {channelDotnetRuntime}");
            return channelDotnetRuntime;
        }

        /// <summary>
        /// Retrieves the current sdk version for a channel
        /// </summary>
        private static async Task<string> GetReleasedSdkChannelVersion(string channel)
        {
            var content = await DownloadContentAsync(_releaseMetadata);

            var index = JObject.Parse(content);
            var channelSdk = index.SelectToken($"$.releases-index[?(@.channel-version == '{channel}')].latest-sdk").ToString();

            Log.WriteLine($"Detecting current SDK version for channel {channel}: {channelSdk}");
            return channelSdk;
        }

        private static async Task<string> ParseLatestVersionFile(string url)
        {
            var content = await DownloadContentAsync(url);

            string latestSdk;
            using (var sr = new StringReader(content))
            {
                sr.ReadLine();
                latestSdk = sr.ReadLine();

            }
            
            return latestSdk;
        }

        /// <summary>
        /// Retrieves the current sdk version for a channel
        /// </summary>
        private static async Task<string> GetLatestSdkChannelVersion(string channel)
        {
            var content = await DownloadContentAsync(_releaseMetadata);

            var index = JObject.Parse(content);
            var channelSdk = index.SelectToken($"$.releases-index[?(@.channel-version == '{channel}')].latest-sdk").ToString();

            Log.WriteLine($"Detecting current SDK version for channel {channel}: {channelSdk}");
            return channelSdk;
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
            if (String.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return;
            }

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

        private static async Task<Process> StartProcess(string hostname, string benchmarksRepo, ServerJob job, string dotnetHome, StringBuilder standardOutput)
        {
            var workingDirectory = Path.Combine(benchmarksRepo, Path.GetDirectoryName(job.Source.Project));
            var scheme = (job.Scheme == Scheme.H2 || job.Scheme == Scheme.Https) ? "https" : "http";
            var serverUrl = $"{scheme}://{hostname}:{job.Port}";
            var executable = GetDotNetExecutable(dotnetHome);
            var projectFilename = Path.GetFileNameWithoutExtension(job.Source.Project);
            var benchmarksDll = Path.Combine("published", $"{projectFilename}.dll");
            var iis = job.WebHost == WebHost.IISInProcess || job.WebHost == WebHost.IISOutOfProcess;

            // Running BeforeScript
            if (!String.IsNullOrEmpty(job.BeforeScript))
            {
                var segments = job.BeforeScript.Split(' ', 2);
                var result = ProcessUtil.Run(segments[0], segments.Length > 1 ? segments[1] : "", workingDirectory: workingDirectory, log: true);
                standardOutput.AppendLine(result.StandardOutput);
            }

            var commandLine = benchmarksDll ?? "";

            if (job.SelfContained)
            {
                workingDirectory = Path.Combine(workingDirectory, "published");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    executable = Path.Combine(workingDirectory, $"{projectFilename}.exe");
                }
                else
                {
                    executable = Path.Combine(workingDirectory, projectFilename);
                }

                commandLine = "";
            }

            job.BasePath = workingDirectory;

            var arguments = $" --nonInteractive true" +
                    $" --scenarios {job.Scenario}";

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
                case WebHost.IISInProcess:
                    arguments += $" --server IISInProcess";
                    break;
                case WebHost.IISOutOfProcess:
                    arguments += $" --server IISOutOfProcess";
                    break;
                default:
                    throw new NotSupportedException("Invalid WebHost value for benchmarks");
            }

            if (job.KestrelThreadCount.HasValue)
            {
                arguments += $" --threadCount {job.KestrelThreadCount.Value}";
            }

            if (!iis)
            {
                arguments += $" --server.urls {serverUrl}";
            }

            arguments += $" --protocol {job.Scheme.ToString().ToLowerInvariant()}";

            commandLine += $" {job.Arguments}";

            if (!job.NoArguments)
            {
                commandLine += $" {arguments}";
            }


            if (iis)
            {
                Log.WriteLine($"Generating application host config for '{executable} {commandLine}'");

                var apphost = GenerateApplicationHostConfig(job, "published", executable, commandLine, hostname);
                commandLine = $"-h \"{apphost}\"";
                executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\inetsrv\w3wp.exe");
            }

            if (job.MemoryLimitInBytes > 0)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Log.WriteLine($"Creating cgroup with memory limits: {job.MemoryLimitInBytes}");

                    var cgcreate = ProcessUtil.Run("cgcreate", "-g memory:benchmarks\"");

                    if (cgcreate.ExitCode > 0)
                    {
                        job.Error += "Could not create cgroup";
                        return null;
                    }

                    ProcessUtil.Run("cgset", $"-r memory.limit_in_bytes={job.MemoryLimitInBytes} benchmarks");

                    commandLine = $"-g memory:benchmarks {executable} {commandLine}";
                    executable = "cgexec";
                }
            }

            Log.WriteLine($"Invoking executable: {executable}, with arguments: {commandLine}");

            var process = new Process()
            {
                StartInfo = {
                    FileName = executable,
                    Arguments = commandLine,
                    WorkingDirectory = workingDirectory,
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

            if (job.Collect && OperatingSystem == OperatingSystem.Linux)
            {
                // c.f. https://github.com/dotnet/coreclr/blob/master/Documentation/project-docs/linux-performance-tracing.md#collecting-a-trace
                // The Task library EventSource events are distorting the trace quite a bit.
                // It is better at least for now to turn off EventSource events when collecting linux data.
                // Thus don’t set COMPlus_EnableEventLog = 1
                process.StartInfo.Environment.Add("COMPlus_PerfMapEnabled", "1");
            }

            foreach(var env in job.EnvironmentVariables)
            {
                Log.WriteLine($"Setting ENV: {env.Key} = {env.Value}");
                process.StartInfo.Environment.Add(env.Key, env.Value);
            }

            var stopwatch = new Stopwatch();

            process.OutputDataReceived += (_, e) =>
            {
                if (e != null && e.Data != null)
                {
                    Log.WriteLine(e.Data);
                    standardOutput.AppendLine(e.Data);

                    if (job.State == ServerState.Starting &&
                        ((!String.IsNullOrEmpty(job.ReadyStateText) && e.Data.IndexOf(job.ReadyStateText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        e.Data.ToLowerInvariant().Contains("started") ||
                        e.Data.ToLowerInvariant().Contains("listening")))
                    {
                        MarkAsRunning(hostname, job, stopwatch);
                    }
                }
            };

            // Start perfview?
            if (job.Collect)
            {
                if (OperatingSystem == OperatingSystem.Windows)
                {
                    job.PerfViewTraceFile = Path.Combine(job.BasePath, "benchmarks.etl.zip");
                    var perfViewArguments = new Dictionary<string, string>();
                    perfViewArguments["AcceptEula"] = "";
                    perfViewArguments["NoGui"] = "";

                    if (!String.IsNullOrEmpty(job.CollectArguments))
                    {
                        foreach (var tuple in job.CollectArguments.Split(';'))
                        {
                            var values = tuple.Split(new char[] { '=' }, 2);
                            perfViewArguments[values[0]] = values.Length > 1 ? values[1] : "";
                        }
                    }

                    var perfviewArguments = $"start";

                    foreach (var customArg in perfViewArguments)
                    {
                        var value = String.IsNullOrEmpty(customArg.Value) ? "" : $"={customArg.Value}";
                        perfviewArguments += $" /{customArg.Key}{value}";
                    }

                    perfviewArguments += $" \"{Path.Combine(job.BasePath, "benchmarks.trace")}\"";
                    RunPerfview(perfviewArguments, Path.Combine(benchmarksRepo, job.BasePath));
                    Log.WriteLine($"Starting PerfView {perfviewArguments}");
                }
                else
                {
                    var perfViewArguments = new Dictionary<string, string>();

                    if (!String.IsNullOrEmpty(job.CollectArguments))
                    {
                        foreach (var tuple in job.CollectArguments.Split(';'))
                        {
                            var values = tuple.Split(new char[] { '=' }, 2);
                            perfViewArguments[values[0]] = values.Length > 1 ? values[1] : "";
                        }
                    }

                    var perfviewArguments = "collect benchmarks";

                    foreach (var customArg in perfViewArguments)
                    {
                        var value = String.IsNullOrEmpty(customArg.Value) ? "" : $" {customArg.Value.ToLowerInvariant()}";
                        perfviewArguments += $" -{customArg.Key}{value}";
                    }

                    job.PerfViewTraceFile = Path.Combine(job.BasePath, "benchmarks.trace.zip");
                    perfCollectProcess = RunPerfcollect(perfviewArguments, Path.Combine(benchmarksRepo, job.BasePath));
                }
            }

            stopwatch.Start();
            process.Start();
            process.BeginOutputReadLine();

            if (job.MemoryLimitInBytes > 0)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Log.WriteLine($"Creating job oject with memory limits: {job.MemoryLimitInBytes}");

                    var manager = new ChildProcessManager(job.MemoryLimitInBytes);
                    manager.AddProcess(process);

                    process.Exited += (sender, e) =>
                    {
                        Log.WriteLine("Releasing job object");
                        manager.Dispose();
                    };
                }
            }

            if (iis)
            {
                await WaitToListen(job, hostname);
                MarkAsRunning(hostname, job, stopwatch);
            }

            return process;
        }

        private static void MarkAsRunning(string hostname, ServerJob job, Stopwatch stopwatch)
        {
            lock (job)
            {
                // Already executed this method?
                if (job.State == ServerState.Running)
                {
                    return;
                }

                job.StartupMainMethod = stopwatch.Elapsed;

                Log.WriteLine($"Running job '{job.Id}' with scenario '{job.Scenario}'");
                job.Url = ComputeServerUrl(hostname, job);

                // Mark the job as running to allow the Client to start the test
                job.State = ServerState.Running;
            }
        }

        private static string GenerateApplicationHostConfig(ServerJob job, string benchmarksBin, string executable, string arguments,
            string hostname)
        {
            void SetAttribute(XDocument doc, string path, string name, string value)
            {
                var element = doc.XPathSelectElement(path);
                if (element == null)
                {
                    throw new InvalidOperationException("Element not found");
                }

                element.SetAttributeValue(name, value);
            }

            using (var resourceStream = Assembly.GetCallingAssembly().GetManifestResourceStream("BenchmarksServer.applicationHost.config"))
            {
                var applicationHostConfig = XDocument.Load(resourceStream);
                SetAttribute(applicationHostConfig, "/configuration/system.webServer/aspNetCore", "processPath", executable);
                SetAttribute(applicationHostConfig, "/configuration/system.webServer/aspNetCore", "arguments", arguments);

                var ancmPath = Path.Combine(job.BasePath, benchmarksBin, "x64\\aspnetcorev2.dll");
                SetAttribute(applicationHostConfig, "/configuration/system.webServer/globalModules/add[@name='AspNetCoreModuleV2']", "image", ancmPath);

                SetAttribute(applicationHostConfig, "/configuration/system.applicationHost/sites/site/bindings/binding", "bindingInformation", $"*:{job.Port}:");
                SetAttribute(applicationHostConfig, "/configuration/system.applicationHost/sites/site/application/virtualDirectory", "physicalPath", job.BasePath);
                //\runtimes\win-x64\nativeassets\netcoreapp2.1\aspnetcorerh.dll

                if (job.WebHost == WebHost.IISInProcess)
                {
                    SetAttribute(applicationHostConfig, "/configuration/system.webServer/aspNetCore", "hostingModel", "inprocess");
                }

                var fileName = executable + ".apphost.config";
                applicationHostConfig.Save(fileName);

                return fileName;
            }
        }

        private static string ComputeServerUrl(string hostname, ServerJob job)
        {
            var scheme = (job.Scheme == Scheme.H2 || job.Scheme == Scheme.Https) ? "https" : "http";
            return $"{scheme}://{hostname}:{job.Port}/{job.Path.TrimStart('/')}";
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

        private static async Task<string> DownloadContentAsync(string url, int maxRetries = 3, int timeout = 5)
        {
            for (var i = 0; i < maxRetries; ++i)
            {
                try
                {
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
                    return await _httpClient.GetStringAsync(url);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while downloading {url}:");
                    Console.WriteLine(e);
                }
            }

            throw new ApplicationException($"Error while downloading {url} after {maxRetries} attempts");
        }

        private static async Task<string> GetLatestPackageVersion(string packageIndexUrl, string versionPrefix)
        {
            Log.WriteLine($"Downloading package metadata ...");
            var index = JObject.Parse(await DownloadContentAsync(packageIndexUrl));

            var compatiblePages = index["items"].Where(t => ((string)t["lower"]).StartsWith(versionPrefix)).ToArray();

            // All versions might be comprised in a single page, with lower and upper bounds not matching the prefix
            if (!compatiblePages.Any())
            {
                compatiblePages = index["items"].ToArray();
            }

            foreach (var page in compatiblePages.Reverse())
            {
                var lastPageUrl = (string)page["@id"];

                var lastPage = JObject.Parse(await DownloadContentAsync(lastPageUrl));

                var entries = packageIndexUrl.Contains("myget", StringComparison.OrdinalIgnoreCase)
                                    ? lastPage["items"]
                                    : lastPage["items"][0]["items"]
                                    ;

                // Extract the highest version
                var lastEntry = entries
                    .Where(t => ((string)t["catalogEntry"]["version"]).StartsWith(versionPrefix)).LastOrDefault();

                if (lastEntry != null)
                {
                    return (string)lastEntry["catalogEntry"]["version"];
                }
            }

            return null;
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

        public enum ErrorModes : uint
        {
            SYSTEM_DEFAULT = 0x0,
            SEM_FAILCRITICALERRORS = 0x0001,
            SEM_NOALIGNMENTFAULTEXCEPT = 0x0004,
            SEM_NOGPFAULTERRORBOX = 0x0002,
            SEM_NOOPENFILEERRORBOX = 0x8000,
            SEM_NONE = SEM_FAILCRITICALERRORS | SEM_NOALIGNMENTFAULTEXCEPT | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX
        }

        [DllImport("kernel32.dll")]
        static extern ErrorModes SetErrorMode(ErrorModes uMode);
    }
}
