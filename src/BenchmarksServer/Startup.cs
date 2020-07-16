// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
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
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Diagnostics.Tools.RuntimeClient;
using Microsoft.Diagnostics.Tools.Trace;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;
using Repository;
using OperatingSystem = Benchmarks.ServerJob.OperatingSystem;

namespace BenchmarkServer
{
    public class Startup
    {
        /*
         * List of accepted values for AspNetCoreVersion and RuntimeVersion
         *
            [Empty] The default channel
            Current The publicly released version
            Latest  The latest transitive version 
            Edge    The latest build
            
            // Legacy, this will be converted automatically to new channel/targetFramework semantics
            2.1     -> current
            2.1.*   -> edge
            2.1.8   -> specific version

            Based on the target framework
         */

        private static string DefaultTargetFramework = "netcoreapp5.0";
        private static string DefaultChannel = "current";

        private const string PerfViewVersion = "P2.0.54";

        private static readonly HttpClient _httpClient;
        private static readonly HttpClientHandler _httpClientHandler;
        private static readonly string _dotnetInstallShUrl = "https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.sh";
        private static readonly string _dotnetInstallPs1Url = "https://raw.githubusercontent.com/dotnet/sdk/master/scripts/obtain/dotnet-install.ps1";
        private static readonly string _aspNetCoreDependenciesUrl = "https://raw.githubusercontent.com/aspnet/AspNetCore/{0}";
        private static readonly string _perfviewUrl = $"https://github.com/Microsoft/perfview/releases/download/{PerfViewVersion}/PerfView.exe";
        private static readonly string _aspnetFlatContainerUrl = "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5/nuget/v3/flat2/Microsoft.AspNetCore.App.Runtime.linux-x64/index.json";

        // Safe-keeping these urls
        //private static readonly string _latestRuntimeApiUrl = "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5/nuget/v3/flat2/Microsoft.NetCore.App.Runtime.linux-x64/index.json";
        //private static readonly string _latestDesktopApiUrl = "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5/nuget/v3/flat2/Microsoft.NetCore.App.Runtime.win-x64/index.json";
        //private static readonly string _releaseMetadata = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json";

        private static readonly string _latestSdkVersionUrl = "https://aka.ms/dotnet/net5/dev/Sdk/productCommit-win-x64.txt";
        private static readonly string _aspnetSdkVersionUrl = "https://raw.githubusercontent.com/dotnet/aspnetcore/master/global.json";
        private static readonly string[] _runtimeFeedUrls = new string[] {
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5/nuget/v3/flat2",
            "https://dotnetfeed.blob.core.windows.net/dotnet-core/flatcontainer",
            "https://api.nuget.org/v3/flatcontainer" };

        // Cached lists of SDKs and runtimes already installed
        private static readonly HashSet<string> _installedAspNetRuntimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _installedDotnetRuntimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _installedDesktopRuntimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _ignoredDesktopRuntimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _installedSdks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private const string _defaultUrl = "http://*:5001";
        private static readonly string _defaultHostname = Environment.MachineName.ToLowerInvariant();
        private static string _perfviewPath;
        private static string _dotnetInstallPath;

        // https://docs.docker.com/config/containers/resource_constraints/
        private const double _defaultDockerCfsPeriod = 100000;

        private static readonly IRepository<ServerJob> _jobs = new InMemoryRepository<ServerJob>();
        private static string _rootTempDir;
        private static string _dotnethome;
        private static bool _cleanup = true;
        private static Process perfCollectProcess;
        private static object _synLock = new object();

        private static Task dotnetTraceTask;
        private static ManualResetEvent dotnetTraceManualReset;

        public static OperatingSystem OperatingSystem { get; }
        public static Hardware Hardware { get; private set; }
        public static string HardwareVersion { get; private set; }
        public static Dictionary<Database, string> ConnectionStrings = new Dictionary<Database, string>();
        public static TimeSpan DriverTimeout = TimeSpan.FromSeconds(10);
        public static TimeSpan StartTimeout = TimeSpan.FromMinutes(3);
        public static TimeSpan BuildTimeout = TimeSpan.FromHours(3);
        public static TimeSpan DeletedTimeout = TimeSpan.FromHours(18);
        public static TimeSpan PerfCollectTimeout = TimeSpan.FromMinutes(2);

        private static string _startPerfviewArguments;

        private static ulong eventPipeSessionId = 0;
        private static Task eventPipeTask = null;
        private static bool eventPipeTerminated = false;

        private static ulong measurementsSessionId = 0;
        private static Task measurementsTask = null;
        private static bool measurementsTerminated = false;

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


            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClientHandler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;

            _httpClient = new HttpClient(_httpClientHandler);
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews().AddNewtonsoftJson();
            services.AddSingleton(_jobs);
        }

        public void Configure(IApplicationBuilder app, IHostApplicationLifetime hostApplicationLifetime)
        {
            hostApplicationLifetime.ApplicationStopping.Register(OnShutdown);

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapDefaultControllerRoute();
            });

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
            var dockerHostnameOption = app.Option("-nd|--docker-hostname", $"Hostname for benchmark server when running Docker on a different hostname.",
                CommandOptionType.SingleValue);
            var hardwareOption = app.Option("--hardware", "Hardware (Cloud or Physical).  Required.",
                CommandOptionType.SingleValue);
            var hardwareVersionOption = app.Option("--hardware-version", "Hardware version (e.g, D3V2, Z420, ...).  Required.",
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
                    HardwareVersion = "Unspecified";
                }
                if (Enum.TryParse(hardwareOption.Value(), ignoreCase: true, result: out Hardware hardware))
                {
                    Hardware = hardware;
                }
                else
                {
                    Hardware = Hardware.Unknown;
                }

                var url = urlOption.HasValue() ? urlOption.Value() : _defaultUrl;
                var hostname = hostnameOption.HasValue() ? hostnameOption.Value() : _defaultHostname;
                var dockerHostname = dockerHostnameOption.HasValue() ? dockerHostnameOption.Value() : hostname;

                return Run(url, hostname, dockerHostname).Result;
            });

            return app.Execute(args);
        }

        private static async Task<int> Run(string url, string hostname, string dockerHostname)
        {
            var isDefaultUrl = url == _defaultUrl;

            // if the default url is used, check if the port is already in use
            if (isDefaultUrl)
            {
                while (true)
                {
                    try
                    {
                        var localCts = new CancellationTokenSource();

                        var localTask = new WebHostBuilder()
                            .UseUrls(url)
                            .UseKestrel()
                            .Configure(_ => { })
                            .ConfigureLogging((hostingContext, logging) =>
                            {
                                logging.ClearProviders();
                            })
                            .Build()
                            .RunAsync(localCts.Token);

                        localCts.Cancel();

                        await localTask;

                        break;
                    }
                    catch (IOException e)
                    {
                        if (e.InnerException is AddressInUseException)
                        {
                            var port = int.Parse(url.Substring(url.LastIndexOf(':') + 1));
                            url = url.Replace(port.ToString(), (port + 1).ToString());
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }

            var host = new WebHostBuilder()
                    .UseKestrel()
                    .ConfigureKestrel(o => o.Limits.MaxRequestBodySize = (long)10 * 1024 * 1024 * 1024)
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
            var processJobsTask = ProcessJobs(hostname, dockerHostname, processJobsCts.Token);

            var completedTask = await Task.WhenAny(hostTask, processJobsTask);

            // Propagate exception (and exit process) if either task faulted
            await completedTask;

            // Host exited normally, so cancel job processor
            processJobsCts.Cancel();
            await processJobsTask;

            return 0;
        }

        private static async Task ProcessJobs(string hostname, string dockerHostname, CancellationToken cancellationToken)
        {

            try
            {
                await EnsureDotnetInstallExistsAsync();

                Log.WriteLine($"Agent ready, waiting for jobs...");

                while (!cancellationToken.IsCancellationRequested)
                {
                    string runId = null;

                    lock (_jobs)
                    {
                        // Lookup expired jobs
                        var expiredJobs = _jobs.GetAll().Where(j => j.State == ServerState.Deleted && DateTime.UtcNow - j.LastDriverCommunicationUtc > DeletedTimeout);

                        foreach (var expiredJob in expiredJobs)
                        {
                            Log.WriteLine($"Removing expired job {expiredJob.Id}");
                            _jobs.Remove(expiredJob.Id);
                        }
                    }

                    /*
                     * New job is created on the agent
                     * Driver still alive?
                     *      yes 
                     *          -> Initializing
                     *          Driver uploads all its attachments, or source files
                     *          Driver send the /start signal -> Waiting  
                     *          Server acknowledges by changing state to -> Starting
                     *          Server builds the application
                     *          -> Running
                     *          A timer is started to track the state of the app, and update its state if it
                     *          fails or the driver isn't responsive
                     *          
                     *      no -> Deleting
                     */

                    // Select the first job that is not yet Deleted, i.e. 
                    var group = new Dictionary<ServerJob, JobTracker>();

                    while (runId == null)
                    {
                        lock (_jobs)
                        {
                            // Looking for the new groupd of jobs
                            var next = _jobs.GetAll().FirstOrDefault(x => x.State == ServerState.New);

                            if (next != null)
                            {
                                runId = next.RunId;

                                foreach (var job in _jobs.GetAll().Where(x => x.RunId == runId))
                                {
                                    group[job] = new JobTracker { job = job };
                                }

                                break;
                            }
                        }

                        await Task.Delay(1000);
                    }

                    while (runId != null)
                    {
                        lock (_jobs)
                        {
                            // Update group to include new ones
                            foreach (var job in _jobs.GetAll().Where(x => x.RunId == runId))
                            {
                                if (!group.ContainsKey(job))
                                {
                                    Log.WriteLine($"Adding job {job.Id} to group");
                                    group[job] = new JobTracker { job = job };
                                }
                            }
                        }

                        // If the running group is all finished, start the next one
                        if (group.Keys.All(x => x.State == ServerState.Deleted))
                        {
                            Log.WriteLine($"All jobs in group are finished");

                            runId = null;
                            break;
                        }

                        foreach (var job in group.Keys)
                        {
                            var tracker = group[job];

                            Log.WriteLine($"Processing job {job.Id} in state {job.State}");

                            // Restore context for the current job
                            Process process = tracker.process;

                            string workingDirectory = tracker.workingDirectory;
                            Timer timer = tracker.timer;
                            var executionLock = tracker.executionLock;
                            var disposed = tracker.disposed;
                            string benchmarksDir = tracker.benchmarksDir;
                            var startMonitorTime = tracker.startMonitorTime;

                            string tempDir = tracker.tempDir;
                            string dockerImage = tracker.dockerImage;
                            string dockerContainerId = tracker.dockerContainerId;

                            eventPipeSessionId = tracker.eventPipeSessionId;
                            eventPipeTask = tracker.eventPipeTask;
                            eventPipeTerminated = tracker.eventPipeTerminated;

                            measurementsSessionId = tracker.measurementsSessionId;
                            measurementsTask = tracker.measurementsTask;
                            measurementsTerminated = tracker.measurementsTerminated;

                            if (job.State == ServerState.New)
                            {
                                var now = DateTime.UtcNow;

                                Log.WriteLine($"Acquiring Job '{job.Id}'");

                                // Ensure all local assets are available
                                await EnsureDotnetInstallExistsAsync();

                                if (now - job.LastDriverCommunicationUtc > DriverTimeout)
                                {
                                    // The job needs to be deleted
                                    Log.WriteLine($"Driver didn't communicate for {DriverTimeout}. Halting job.");
                                    Log.WriteLine($"{job.State} -> Deleting");
                                    job.State = ServerState.Deleting;
                                }
                                else
                                {
                                    startMonitorTime = DateTime.UtcNow;
                                    Log.WriteLine($"{job.State} -> Initializing");
                                    job.State = ServerState.Initializing;
                                }

                                lock (job.Metadata)
                                {
                                    if (!job.Metadata.Any(x => x.Name == "benchmarks/cpu"))
                                    {
                                        job.Metadata.Enqueue(new MeasurementMetadata
                                        {
                                            Source = "Host Process",
                                            Name = "benchmarks/cpu",
                                            Aggregate = Operation.Max,
                                            Reduce = Operation.Max,
                                            Format = "n0",
                                            LongDescription = "Amount of time the process has utilized the CPU out of 100%",
                                            ShortDescription = "CPU Usage (%)"
                                        });
                                    }

                                    if (!job.Metadata.Any(x => x.Name == "benchmarks/cpu/raw"))
                                    {
                                        job.Metadata.Enqueue(new MeasurementMetadata
                                        {
                                            Source = "Host Process",
                                            Name = "benchmarks/cpu/raw",
                                            Aggregate = Operation.Max,
                                            Reduce = Operation.Max,
                                            Format = "n2", // two decimals
                                            LongDescription = "Raw CPU value (not normalized by number of cores)",
                                            ShortDescription = "Raw CPU Usage (%)"
                                        });
                                    }

                                    if (!job.Metadata.Any(x => x.Name == "benchmarks/working-set"))
                                    {
                                        job.Metadata.Enqueue(new MeasurementMetadata
                                        {
                                            Source = "Host Process",
                                            Name = "benchmarks/working-set",
                                            Aggregate = Operation.Max,
                                            Reduce = Operation.Max,
                                            Format = "n0",
                                            LongDescription = "Amount of working set used by the process (MB)",
                                            ShortDescription = "Working Set (MB)"
                                        });
                                    }

                                    if (!job.Metadata.Any(x => x.Name == "benchmarks/build-time"))
                                    {
                                        job.Metadata.Enqueue(new MeasurementMetadata
                                        {
                                            Source = "Host Process",
                                            Name = "benchmarks/build-time",
                                            Aggregate = Operation.Max,
                                            Reduce = Operation.Max,
                                            Format = "n0",
                                            LongDescription = "How long it took to build the application",
                                            ShortDescription = "Build Time (ms)"
                                        });
                                    }

                                    if (!job.Metadata.Any(x => x.Name == "benchmarks/start-time"))
                                    {
                                        job.Metadata.Enqueue(new MeasurementMetadata
                                        {
                                            Source = "Host Process",
                                            Name = "benchmarks/start-time",
                                            Aggregate = Operation.Max,
                                            Reduce = Operation.Max,
                                            Format = "n0",
                                            LongDescription = "How long it took to start the application",
                                            ShortDescription = "Start Time (ms)"
                                        });
                                    }

                                    if (!job.Metadata.Any(x => x.Name == "benchmarks/published-size"))
                                    {
                                        job.Metadata.Enqueue(new MeasurementMetadata
                                        {
                                            Source = "Host Process",
                                            Name = "benchmarks/published-size",
                                            Aggregate = Operation.Max,
                                            Reduce = Operation.Max,
                                            Format = "n0",
                                            LongDescription = "The size of the published application",
                                            ShortDescription = "Published Size (KB)"
                                        });
                                    }

                                    if (!job.Metadata.Any(x => x.Name == "benchmarks/swap"))
                                    {
                                        job.Metadata.Enqueue(new MeasurementMetadata
                                        {
                                            Source = "Host Process",
                                            Name = "benchmarks/swap",
                                            Aggregate = Operation.Delta,
                                            Reduce = Operation.Max,
                                            Format = "n0",
                                            LongDescription = "Amount of swapped memory (MB)",
                                            ShortDescription = "Swap (MB)"
                                        });
                                    }

                                }
                            }
                            else if (job.State == ServerState.Failed)
                            {
                                var now = DateTime.UtcNow;

                                // Clean the job in case the driver is not running
                                if (now - job.LastDriverCommunicationUtc > DriverTimeout)
                                {
                                    Log.WriteLine($"Driver didn't communicate for {DriverTimeout}. Halting job.");
                                    Log.WriteLine($"{job.State} -> Deleting");
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
                                        Log.WriteLine($"{job.State} -> NotSupported");
                                        job.State = ServerState.NotSupported;
                                        continue;
                                    }

                                    Log.WriteLine($"Starting job '{job.Id}' with scenario '{job.Scenario}'");
                                    Log.WriteLine($"{job.State} -> Starting");
                                    job.State = ServerState.Starting;

                                    startMonitorTime = DateTime.UtcNow;

                                    Debug.Assert(tempDir == null);
                                    tempDir = GetTempDir();
                                    workingDirectory = null;
                                    dockerImage = null;

                                    var buildStart = DateTime.UtcNow;
                                    var cts = new CancellationTokenSource();

                                    if (job.Source.IsDocker())
                                    {
                                        try
                                        {
                                            var buildAndRunTask = Task.Run(() => DockerBuildAndRun(tempDir, job, dockerHostname, cancellationToken: cts.Token));

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
                                                    workingDirectory = null;

                                                    Log.WriteLine($"Driver didn't communicate for {DriverTimeout}. Halting build.");
                                                    cts.Cancel();
                                                    await buildAndRunTask;

                                                    Log.WriteLine($"{job.State} -> Failed");
                                                    job.State = ServerState.Failed;
                                                    break;
                                                }

                                                // Cancel the build if it's taking too long
                                                if (DateTime.UtcNow - buildStart > BuildTimeout)
                                                {
                                                    workingDirectory = null;

                                                    Log.WriteLine($"Build is taking too long. Halting build.");
                                                    cts.Cancel();
                                                    await buildAndRunTask;

                                                    job.Error = "Build is taking too long. Halting build.";
                                                    Log.WriteLine($"{job.State} -> Failed");
                                                    job.State = ServerState.Failed;
                                                    break;
                                                }

                                                await Task.Delay(1000);
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            workingDirectory = null;
                                            Log.WriteLine($"Job failed with DockerBuildAndRun: " + e.Message);
                                            Log.WriteLine($"{job.State} -> Failed");
                                            job.State = ServerState.Failed;
                                        }
                                    }
                                    else
                                    {
                                        try
                                        {
                                            // returns the application directory and the dotnet directory to use
                                            var cloneRestoreAndBuildTask = CloneRestoreAndBuild(tempDir, job, _dotnethome, cancellationToken: cts.Token);

                                            while (true)
                                            {
                                                if (cloneRestoreAndBuildTask.IsCompleted)
                                                {
                                                    benchmarksDir = cloneRestoreAndBuildTask.Result;
                                                    break;
                                                }

                                                // Cancel the build if the driver timed out
                                                if (DateTime.UtcNow - job.LastDriverCommunicationUtc > DriverTimeout)
                                                {
                                                    workingDirectory = null;

                                                    Log.WriteLine($"Driver didn't communicate for {DriverTimeout}. Halting build.");
                                                    cts.Cancel();
                                                    await cloneRestoreAndBuildTask;

                                                    Log.WriteLine($"{job.State} -> Failed");
                                                    job.State = ServerState.Failed;
                                                    break;
                                                }

                                                // Cancel the build if it's taking too long
                                                if (DateTime.UtcNow - buildStart > BuildTimeout)
                                                {
                                                    workingDirectory = null;

                                                    Log.WriteLine($"Build is taking too long. Halting build.");
                                                    cts.Cancel();
                                                    await cloneRestoreAndBuildTask;

                                                    job.Error = "Build is taking too long. Halting build.";
                                                    Log.WriteLine($"{job.State} -> Failed");
                                                    job.State = ServerState.Failed;
                                                    break;
                                                }

                                                await Task.Delay(1000);
                                            }

                                        }
                                        finally
                                        {
                                            if (benchmarksDir != null)
                                            {
                                                try
                                                {
                                                    process = await StartProcess(hostname, Path.Combine(tempDir, benchmarksDir), job, _dotnethome);

                                                    job.ProcessId = process.Id;

                                                    Log.WriteLine($"Process started: {process.Id}");

                                                    workingDirectory = process.StartInfo.WorkingDirectory;
                                                }
                                                catch (Exception e)
                                                {
                                                    workingDirectory = null;
                                                    Log.WriteLine($"Job failed while starting");
                                                    Log.WriteLine($"{job.State} -> Failed");
                                                    job.Error = "Server error: " + e.Message;
                                                    job.State = ServerState.Failed;
                                                }
                                            }
                                            else
                                            {
                                                workingDirectory = null;
                                                Log.WriteLine($"Job failed while building");
                                                Log.WriteLine($"{job.State} -> Failed");
                                                job.State = ServerState.Failed;
                                            }
                                        }
                                    }

                                    startMonitorTime = DateTime.UtcNow;
                                    var lastMonitorTime = startMonitorTime;
                                    var oldCPUTime = TimeSpan.Zero;

                                    if (workingDirectory != null)
                                    {
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

                                            try
                                            {
                                                var now = DateTime.UtcNow;

                                                // Stops the job in case the driver is not running
                                                if (now - job.LastDriverCommunicationUtc > DriverTimeout)
                                                {
                                                    Log.WriteLine($"[Heartbeat] Driver didn't communicate for {DriverTimeout}. Halting job.");
                                                    if (job.State == ServerState.Running || job.State == ServerState.TraceCollected)
                                                    {
                                                        Log.WriteLine($"{job.State} -> Stopping");
                                                        job.State = ServerState.Stopping;
                                                    }
                                                }

                                                if (!String.IsNullOrEmpty(dockerImage))
                                                {
                                                    // Check the container is still running
                                                    var inspectResult = ProcessUtil.Run("docker", "inspect -f {{.State.Running}} " + dockerContainerId,
                                                        captureOutput: true,
                                                        log: false, throwOnError: false);

                                                    if (String.Equals(inspectResult.StandardOutput.Trim(), "false"))
                                                    {
                                                        Log.WriteLine($"The Docker container has stopped");
                                                        Log.WriteLine($"{job.State} -> Stopping");
                                                        job.State = ServerState.Stopping;
                                                    }
                                                    else
                                                    {
                                                        // Get docker stats
                                                        var result = ProcessUtil.Run("docker", "container stats --no-stream --format \"{{.CPUPerc}}-{{.MemUsage}}\" " + dockerContainerId,
                                                                log: false, throwOnError: false, captureOutput: true, captureError: true);

                                                        var stats = result.StandardOutput;

                                                        if (!String.IsNullOrEmpty(stats))
                                                        {
                                                            var data = stats.Trim().Split('-');

                                                            // Format is {value}%
                                                            var cpuPercentRaw = data[0];

                                                            // Format is {used}M/GiB/{total}M/GiB
                                                            var workingSetRaw = data[1];
                                                            var usedMemoryRaw = workingSetRaw.Split('/')[0].Trim();
                                                            var cpu = double.Parse(cpuPercentRaw.Trim('%'));
                                                            var rawCPU = cpu;

                                                            // On Windows the CPU already takes the number or HT into account
                                                            if (OperatingSystem == OperatingSystem.Linux)
                                                            {
                                                                cpu = cpu / Environment.ProcessorCount;
                                                            }

                                                            cpu = Math.Round(cpu);

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
                                                                CpuPercentage = cpu
                                                            });

                                                            job.Measurements.Enqueue(new Measurement
                                                            {
                                                                Name = "benchmarks/working-set",
                                                                Timestamp = now,
                                                                Value = Math.Ceiling((double)workingSet / 1024 / 1024) // < 1MB still needs to appear as 1MB
                                                            });

                                                            job.Measurements.Enqueue(new Measurement
                                                            {
                                                                Name = "benchmarks/cpu",
                                                                Timestamp = now,
                                                                Value = cpu
                                                            });

                                                            job.Measurements.Enqueue(new Measurement
                                                            {
                                                                Name = "benchmarks/cpu/raw",
                                                                Timestamp = now,
                                                                Value = rawCPU
                                                            });

                                                            if (OperatingSystem == OperatingSystem.Linux)
                                                            {
                                                                try
                                                                {
                                                                    job.Measurements.Enqueue(new Measurement
                                                                    {
                                                                        Name = "benchmarks/swap",
                                                                        Timestamp = now,
                                                                        Value = (int)GetSwapBytes() / 1024 / 1024
                                                                    });
                                                                }
                                                                catch (Exception e)
                                                                {
                                                                    Log.WriteLine($"[ERROR] Could not get swap memory:" + e.ToString());
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                else if (process != null)
                                                {
                                                    if (process.HasExited)
                                                    {
                                                        if (process.ExitCode != 0)
                                                        {
                                                            Log.WriteLine($"Job failed");

                                                            job.Error = $"Job failed at runtime:\n{job.Output}";

                                                            if (job.State != ServerState.Deleting)
                                                            {
                                                                Log.WriteLine($"{job.State} -> Failed");
                                                                job.State = ServerState.Failed;
                                                            }
                                                        }
                                                        else
                                                        {
                                                            Log.WriteLine($"Process has exited ({process.ExitCode})");

                                                            // Don't revert a Deleting state by mistake
                                                            if (job.State != ServerState.Deleting)
                                                            {
                                                                Log.WriteLine($"{job.State} -> Stopped");
                                                                job.State = ServerState.Stopped;
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {

                                                        // TODO: Accessing the TotalProcessorTime on OSX throws so just leave it as 0 for now
                                                        // We need to dig into this

                                                        var trackProcess = job.ChildProcessId == 0
                                                            ? process
                                                            : Process.GetProcessById(job.ChildProcessId)
                                                            ;

                                                        var newCPUTime = OperatingSystem == OperatingSystem.OSX
                                                            ? TimeSpan.Zero
                                                            : trackProcess.TotalProcessorTime;

                                                        var elapsed = now.Subtract(lastMonitorTime).TotalMilliseconds;
                                                        var rawCpu = (newCPUTime - oldCPUTime).TotalMilliseconds / elapsed * 100;
                                                        var cpu = Math.Round(rawCpu / Environment.ProcessorCount);
                                                        lastMonitorTime = now;

                                                        trackProcess.Refresh();

                                                        // Ignore first measure
                                                        if (oldCPUTime != TimeSpan.Zero && cpu <= 100)
                                                        {
                                                            job.AddServerCounter(new ServerCounter
                                                            {
                                                                Elapsed = now - startMonitorTime,
                                                                WorkingSet = trackProcess.WorkingSet64,
                                                                CpuPercentage = cpu
                                                            });

                                                            job.Measurements.Enqueue(new Measurement
                                                            {
                                                                Name = "benchmarks/working-set",
                                                                Timestamp = now,
                                                                Value = Math.Ceiling((double)trackProcess.WorkingSet64 / 1024 / 1024) // < 1MB still needs to appear as 1MB
                                                            });

                                                            job.Measurements.Enqueue(new Measurement
                                                            {
                                                                Name = "benchmarks/cpu",
                                                                Timestamp = now,
                                                                Value = cpu
                                                            });

                                                            job.Measurements.Enqueue(new Measurement
                                                            {
                                                                Name = "benchmarks/cpu/raw",
                                                                Timestamp = now,
                                                                Value = rawCpu
                                                            });
                                                        }

                                                        oldCPUTime = newCPUTime;

                                                        if (OperatingSystem == OperatingSystem.Linux)
                                                        {
                                                            try
                                                            {
                                                                job.Measurements.Enqueue(new Measurement
                                                                {
                                                                    Name = "benchmarks/swap",
                                                                    Timestamp = now,
                                                                    Value = (int)GetSwapBytes() / 1024 / 1024
                                                                });
                                                            }
                                                            catch (Exception e)
                                                            {
                                                                Log.WriteLine($"[ERROR] Could not get swap memory:" + e.ToString());
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                // Resume once we finished processing all connections
                                                timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                                            }
                                        }
                                        finally
                                        {
                                            // Exit the lock now
                                            Monitor.Exit(executionLock);
                                        }
                                    }, null, TimeSpan.FromTicks(0), TimeSpan.FromSeconds(1));
                                        disposed = false;
                                    }
                                }
                                catch (Exception e)
                                {
                                    Log.WriteLine($"Error starting job '{job.Id}': {e}");
                                    Log.WriteLine($"{job.State} -> Failed");
                                    job.State = ServerState.Failed;
                                    continue;
                                }
                            }
                            else if (job.State == ServerState.Stopping)
                            {
                                Log.WriteLine($"Stopping job '{job.Id}' with scenario '{job.Scenario}'");

                                await StopJobAsync();
                            }
                            else if (job.State == ServerState.Stopped)
                            {
                                Log.WriteLine($"Job '{job.Id}' has stopped, waiting for the driver to delete it");

                                if (DateTime.UtcNow - job.LastDriverCommunicationUtc > DriverTimeout)
                                {
                                    // The job needs to be deleted
                                    Log.WriteLine($"Driver didn't communicate for {DriverTimeout}. Halting job.");
                                    Log.WriteLine($"{job.State} -> Deleting");
                                    job.State = ServerState.Deleting;
                                }
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
                                        RunPerfview($"stop /AcceptEula /NoNGenRundown /NoView {_startPerfviewArguments}", Path.Combine(tempDir, benchmarksDir));
                                    }
                                    else if (OperatingSystem == OperatingSystem.Linux)
                                    {
                                        await StopPerfcollectAsync(perfCollectProcess);
                                    }

                                    Log.WriteLine("Trace collected");
                                    Log.WriteLine($"{job.State} ->  TraceCollected");
                                    job.State = ServerState.TraceCollected;
                                }

                                // Stop dotnet-trace
                                if (job.DotNetTrace)
                                {
                                    if (dotnetTraceTask != null)
                                    {
                                        if (!dotnetTraceTask.IsCompleted)
                                        {
                                            Log.WriteLine("Stopping dotnet-trace");

                                            dotnetTraceManualReset.Set();

                                            await dotnetTraceTask;

                                            dotnetTraceManualReset = null;
                                            dotnetTraceTask = null;
                                        }


                                        Log.WriteLine("Trace collected");
                                    }
                                    else
                                    {
                                        Log.WriteLine("Trace collection aborted, dotnet-trace was not started");
                                    }

                                    Log.WriteLine($"{job.State} ->  TraceCollected");
                                    job.State = ServerState.TraceCollected;
                                }

                            }
                            else if (job.State == ServerState.Starting)
                            {
                                var startTimeout = job.StartTimeout > TimeSpan.Zero
                                    ? job.StartTimeout
                                    : StartTimeout
                                    ;

                                if (DateTime.UtcNow - startMonitorTime > startTimeout)
                                {
                                    Log.WriteLine($"Job didn't start during the expected delay");
                                    job.State = ServerState.Failed;
                                    job.Error = "Job didn't start during the expected delay. Check that it outputs a startup message on the log.";
                                }

                                // Check the driver is still communicating
                                if (DateTime.UtcNow - job.LastDriverCommunicationUtc > DriverTimeout)
                                {
                                    // The job needs to be deleted
                                    Log.WriteLine($"Driver didn't communicate for {DriverTimeout}. Halting job.");
                                    Log.WriteLine($"{job.State} -> Deleting");
                                    job.State = ServerState.Deleting;
                                }
                            }
                            else if (job.State == ServerState.Initializing)
                            {
                                // Check the driver is still communicating
                                if (DateTime.UtcNow - job.LastDriverCommunicationUtc > DriverTimeout)
                                {
                                    // The job needs to be deleted
                                    Log.WriteLine($"Driver didn't communicate for {DriverTimeout}. Halting job.");
                                    Log.WriteLine($"{job.State} -> Deleting");
                                    job.State = ServerState.Deleting;
                                }
                            }

                            async Task StopJobAsync()
                            {
                                // Delete the benchmarks group
                                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && (job.MemoryLimitInBytes > 0 || job.CpuLimitRatio > 0 || !String.IsNullOrEmpty(job.CpuSet)))
                                {
                                    var controller = GetCGroupController(job);

                                    ProcessUtil.Run("cgdelete", $"cpu,memory,cpuset:{controller}", log: true, throwOnError: false);
                                }

                                // Check if we already passed here
                                if (timer == null)
                                {
                                    return;
                                }

                                // Releasing EventPipe
                                if (eventPipeTask != null)
                                {
                                    try
                                    {
                                        Log.WriteLine($"Stopping counter event pipes for job {job.Id}");
                                        if (process != null && !eventPipeTerminated && !process.HasExited)
                                        {
                                            EventPipeClient.StopTracing(process.Id, eventPipeSessionId);
                                        }
                                    }
                                    catch (EndOfStreamException)
                                    {
                                        // If the app we're monitoring exits abruptly, this may throw in which case we just swallow the exception and exit gracefully.
                                    }
                                }

                                // Releasing Measurements
                                if (measurementsTask != null)
                                {
                                    try
                                    {
                                        Log.WriteLine($"Stopping measurement event pipes for job {job.Id}");
                                        if (process != null && !eventPipeTerminated && !process.HasExited)
                                        {
                                            EventPipeClient.StopTracing(process.Id, measurementsSessionId);
                                        }
                                    }
                                    catch (EndOfStreamException)
                                    {
                                        // If the app we're monitoring exits abruptly, this may throw in which case we just swallow the exception and exit gracefully.
                                    }
                                }

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
                                            await StopPerfcollectAsync(perfCollectProcess);
                                        }
                                    }

                                    if (job.DotNetTrace)
                                    {
                                        // Stop dotnet-trace if still active
                                        if (dotnetTraceTask != null)
                                        {
                                            if (!dotnetTraceTask.IsCompleted)
                                            {
                                                Log.WriteLine("Stopping dotnet-trace");

                                                dotnetTraceManualReset.Set();

                                                await dotnetTraceTask;

                                                dotnetTraceManualReset = null;
                                                dotnetTraceTask = null;
                                            }
                                        }
                                    }

                                    if (OperatingSystem == OperatingSystem.Linux)
                                    {
                                        Log.WriteLine($"Invoking SIGINT ...");

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
                                            Log.WriteLine($"/shutdown endpoint failed... ({job.Url}/shutdown)");
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

                                    DockerCleanUp(dockerContainerId, dockerImage, job);
                                }

                                // Running AfterScript
                                if (!String.IsNullOrEmpty(job.AfterScript))
                                {
                                    var segments = job.AfterScript.Split(' ', 2);
                                    var processResult = ProcessUtil.Run(segments[0], segments.Length > 1 ? segments[1] : "", log: true, workingDirectory: workingDirectory);

                                    // TODO: Update the output with the result of AfterScript, and change the driver so that it polls the job a last time even when the job is stopped
                                    // if there is an AfterScript
                                }

                                Log.WriteLine($"Process stopped ({job.State})");
                            }

                            async Task DeleteJobAsync()
                            {
                                await StopJobAsync();

                                if (_cleanup && !job.NoClean && tempDir != null)
                                {
                                    TryDeleteDir(tempDir, false);
                                }

                                tempDir = null;

                                Log.WriteLine($"{job.State} -> Deleted");

                                job.State = ServerState.Deleted;
                            }

                            tracker.process = process;

                            tracker.workingDirectory = workingDirectory;
                            tracker.timer = timer;
                            tracker.executionLock = executionLock;
                            tracker.disposed = disposed;
                            tracker.benchmarksDir = benchmarksDir;
                            tracker.startMonitorTime = startMonitorTime;

                            tracker.tempDir = tempDir;
                            tracker.dockerImage = dockerImage;
                            tracker.dockerContainerId = dockerContainerId;

                            tracker.eventPipeSessionId = eventPipeSessionId;
                            tracker.eventPipeTask = eventPipeTask;
                            tracker.eventPipeTerminated = eventPipeTerminated;

                            tracker.measurementsSessionId = measurementsSessionId;
                            tracker.measurementsTask = measurementsTask;
                            tracker.measurementsTerminated = measurementsTerminated;

                            await Task.Delay(1000);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.WriteLine($"Unnexpected error: {e.ToString()}");
            }
        }

        private static string RunPerfview(string arguments, string workingDirectory)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Log.WriteLine($"PerfView is only supported on Windows");
                return null;
            }

            Log.WriteLine($"Starting process '{_perfviewPath} {arguments}' in '{workingDirectory}'");

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

            if (perfCollectProcess == null || perfCollectProcess.HasExited)
            {
                Log.WriteLine($"PerfCollect is not running");
                return;
            }

            var processId = perfCollectProcess.Id;

            Log.WriteLine($"Stopping PerfCollect");

            Mono.Unix.Native.Syscall.kill(processId, Mono.Unix.Native.Signum.SIGINT);

            // Max delay for perfcollect to stop
            var delay = Task.Delay(PerfCollectTimeout);

            while (!perfCollectProcess.HasExited && !delay.IsCompletedSuccessfully)
            {
                await Task.Delay(1000);
            }

            if (!perfCollectProcess.HasExited)
            {
                Log.WriteLine($"PerfCollect exceeded allowed time, stopping ...");
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

        private static void ConvertLines(string path)
        {
            Log.WriteLine($"Converting '{path}' ...");

            var content = File.ReadAllText(path);

            if (path.IndexOf("\r\n") >= 0)
            {
                File.WriteAllText(path, path.Replace("\r\n", "\n"));
            }
        }

        private static async Task<(string containerId, string imageName, string workingDirectory)> DockerBuildAndRun(string path, ServerJob job, string hostname, CancellationToken cancellationToken = default(CancellationToken))
        {
            var source = job.Source;
            string srcDir;

            // Docker image names must be lowercase
            var imageName = source.GetNormalizedImageName();

            if (source.SourceCode != null)
            {
                srcDir = Path.Combine(path, "src");
                Log.WriteLine($"Extracting source code to {srcDir}");

                ZipFile.ExtractToDirectory(job.Source.SourceCode.TempFilename, srcDir);

                // Convert CRLF to LF on Linux
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Log.WriteLine($"Converting text files ...");

                    foreach (var file in Directory.GetFiles(srcDir + Path.DirectorySeparatorChar, "*.*", SearchOption.AllDirectories))
                    {
                        ConvertLines(file);
                    }
                }

                File.Delete(job.Source.SourceCode.TempFilename);
            }
            else if (!String.IsNullOrEmpty(source.Repository))
            {
                var branchAndCommit = source.BranchOrCommit.Split('#', 2);

                var dir = Git.Clone(path, source.Repository, shallow: branchAndCommit.Length == 1, branch: branchAndCommit[0]);

                srcDir = Path.Combine(path, dir);

                if (branchAndCommit.Length > 1)
                {
                    Git.Checkout(srcDir, branchAndCommit[1]);
                }

                if (source.InitSubmodules)
                {
                    Git.InitSubModules(srcDir);
                }
            }
            else
            {
                srcDir = path;
            }

            if (String.IsNullOrEmpty(source.DockerContextDirectory))
            {
                source.DockerContextDirectory = Path.GetDirectoryName(source.DockerFile).Replace("\\", "/");
            }

            var workingDirectory = Path.Combine(srcDir, source.DockerContextDirectory);

            job.BasePath = workingDirectory;

            // Running BeforeScript
            if (!String.IsNullOrEmpty(job.BeforeScript))
            {
                var segments = job.BeforeScript.Split(' ', 2);
                var processResult = ProcessUtil.Run(segments[0], segments.Length > 1 ? segments[1] : "", workingDirectory: workingDirectory, log: true, outputDataReceived: text => job.Output.AddLine(text));
            }

            // Copy build files before building/publishing
            foreach (var attachment in job.BuildAttachments)
            {
                var filename = Path.Combine(srcDir, attachment.Filename.Replace("\\", "/"));

                Log.WriteLine($"Creating build file: {filename}");

                if (File.Exists(filename))
                {
                    File.Delete(filename);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(filename));

                File.Copy(attachment.TempFilename, filename);
                File.Delete(attachment.TempFilename);
            }

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            // The DockerLoad argument contains the path of a tar file that can be loaded
            if (String.IsNullOrEmpty(source.DockerLoad))
            {
                string buildParameters = "";

                // Apply custom build arguments sent from the driver
                foreach (var argument in job.BuildArguments)
                {
                    buildParameters += $"--build-arg {argument} ";
                }

                var dockerBuildArguments = $"build --pull {buildParameters} -t {imageName} -f {source.DockerFile} {workingDirectory}";

                job.BuildLog.AddLine("docker " + dockerBuildArguments);

                var buildResults = ProcessUtil.Run("docker", dockerBuildArguments,
                    workingDirectory: srcDir,
                    timeout: BuildTimeout,
                    cancellationToken: cancellationToken,
                    log: true,
                    outputDataReceived: text => job.BuildLog.AddLine(text)
                    );

                stopwatch.Stop();

                job.BuildTime = stopwatch.Elapsed;

                job.Measurements.Enqueue(new Measurement
                {
                    Name = "benchmarks/build-time",
                    Timestamp = DateTime.UtcNow,
                    Value = stopwatch.ElapsedMilliseconds
                });

                stopwatch.Reset();

                if (buildResults.ExitCode != 0)
                {
                    job.Error = job.BuildLog.ToString();
                }
            }
            else
            {
                Log.WriteLine($"Loading docker image {source.DockerLoad} from {srcDir}");

                var dockerLoadArguments = $"load -i {source.DockerLoad} ";

                job.BuildLog.AddLine("docker " + dockerLoadArguments);

                ProcessUtil.Run("docker", dockerLoadArguments, workingDirectory: srcDir, timeout: BuildTimeout, cancellationToken: cancellationToken, log: true);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return (null, null, null);
            }

            var environmentArguments = "";

            foreach (var env in job.EnvironmentVariables)
            {
                environmentArguments += $"--env {env.Key}={env.Value} ";
            }

            var containerName = $"{imageName}-{job.Id}";

            // TODO: Clean previous images 

            // Stop container in case it failed to stop earlier
            // ProcessUtil.Run("docker", $"stop {containerName}", throwOnError: false);

            // Delete container if the same name already exists
            // ProcessUtil.Run("docker", $"rm {containerName}", throwOnError: false);

            var command = OperatingSystem == OperatingSystem.Linux
                ? $"run -d {environmentArguments} {job.Arguments} --label benchmarks --mount type=bind,source=/mnt,target=/tmp --name {containerName} --privileged --network host {imageName} {source.DockerCommand}"
                : $"run -d {environmentArguments} {job.Arguments} --label benchmarks --name {containerName} --network SELF --ip {hostname} {imageName} {source.DockerCommand}";

            if (job.Collect && job.CollectStartup)
            {
                StartCollection(workingDirectory, job);
            }

            job.BuildLog.AddLine("docker " + command);

            var result = ProcessUtil.Run("docker", $"{command} ", throwOnError: false, onStart: () => stopwatch.Start(), captureOutput: true);

            var containerId = result.StandardOutput.Trim();

            job.Url = ComputeServerUrl(hostname, job);

            Log.WriteLine($"Intercepting Docker logs for '{containerId}' ...");

            var process = new Process()
            {
                StartInfo = {
                    FileName = "docker",
                    Arguments = $"logs -f {containerId}",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
                EnableRaisingEvents = true
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!String.IsNullOrEmpty(job.ReadyStateText))
            {
                Log.WriteLine($"Waiting for startup signal: '{job.ReadyStateText}'...");

                process.OutputDataReceived += (_, e) =>
                {
                    if (e != null && e.Data != null)
                    {
                        Log.WriteLine(e.Data);

                        job.Output.AddLine(e.Data);

                        if (job.State == ServerState.Starting && e.Data.IndexOf(job.ReadyStateText, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Log.WriteLine($"Ready state detected, application is now running...");
                            MarkAsRunning(hostname, job, stopwatch);

                            if (job.Collect && !job.CollectStartup)
                            {
                                StartCollection(workingDirectory, job);
                            }
                        }

                        ParseMeasurementOutput(job, job.Output);
                    }
                };

                // Also listen on the error output
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e != null && e.Data != null)
                    {
                        Log.WriteLine("[STDERR] " + e.Data);

                        job.Output.AddLine("[STDERR] " + e.Data);

                        if (job.State == ServerState.Starting && e.Data.IndexOf(job.ReadyStateText, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Log.WriteLine($"Ready state detected, application is now running...");
                            MarkAsRunning(hostname, job, stopwatch);

                            if (job.Collect && !job.CollectStartup)
                            {
                                StartCollection(workingDirectory, job);
                            }
                        }
                    }
                };
            }
            else
            {
                Log.WriteLine($"Trying to contact the application ...");

                process.OutputDataReceived += (_, e) =>
                {
                    if (e != null && e.Data != null)
                    {
                        Log.WriteLine(e.Data);
                        job.Output.AddLine(e.Data);

                        ParseMeasurementOutput(job, job.Output);
                    }
                };

                // Wait until the service is reachable to avoid races where the container started but isn't
                // listening yet. If it keeps failing we ignore it. If the port is unreachable then clients
                // will fail to connect and the job will be cleaned up properly
                if (await WaitToListen(job, hostname, 30))
                {
                    Log.WriteLine($"Application is responding...");
                }
                else
                {
                    Log.WriteLine($"Application MAY be running, continuing...");
                }

                MarkAsRunning(hostname, job, stopwatch);

                if (job.Collect && !job.CollectStartup)
                {
                    StartCollection(workingDirectory, job);
                }
            }

            return (containerId, imageName, workingDirectory);
        }

        private static void ParseMeasurementOutput(ServerJob job, RollingLog standardOutput)
        {

            // Detected custom statistics in stdout, parse it
            if (standardOutput.LastLine.IndexOf("#EndJobStatistics", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Seek the beginning of statistics

                var lines = standardOutput.Get(0);

                var startIndex = lines.Length - 1;

                // Seek backward in case thre are multiple blocks of statistics
                for (; startIndex >= 0; startIndex--)
                {
                    if (lines[startIndex].Contains("#StartJobStatistics", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }

                if (startIndex == lines.Length - 1)
                {
                    Log.WriteLine($"Didn't find start of statistics");
                    return;
                }
                else
                {
                    Log.WriteLine($"Parsing custom measures...");
                }

                var jsonStatistics = String.Join(Environment.NewLine, lines.Skip(startIndex + 1).Take(lines.Length - startIndex - 2));

                try
                {
                    var jobStatistics = JsonConvert.DeserializeObject<JobStatistics>(jsonStatistics);

                    Log.WriteLine($"Found {jobStatistics.Metadata.Count} metadata and {jobStatistics.Measurements.Count} measurements");

                    foreach (var metadata in jobStatistics.Metadata)
                    {
                        job.Metadata.Enqueue(metadata);
                    }

                    foreach (var measurement in jobStatistics.Measurements)
                    {
                        job.Measurements.Enqueue(measurement);
                    }
                }
                catch (Exception e)
                {
                    Log.WriteLine($"[ERROR] Invalid Json payload: " + e.Message);
                }
            }
        }

        private static async Task<bool> WaitToListen(ServerJob job, string hostname, int maxRetries = 5)
        {
            if (job.IsConsoleApp)
            {
                Log.WriteLine($"Console application detected, not waiting");
                return true;
            }

            Log.WriteLine($"Polling server on {hostname}:{job.Port}");

            for (var i = 1; i <= maxRetries; ++i)
            {
                try
                {
                    using (var tcpClient = new TcpClient())
                    {
                        var connectTask = tcpClient.ConnectAsync(hostname, job.Port);
                        await Task.WhenAny(connectTask, Task.Delay(1000));
                        if (connectTask.IsCompleted)
                        {
                            Log.WriteLine($"Success!");
                            return true;
                        }

                        Log.WriteLine($"Attempt #{i} failed...");
                    }
                }
                catch
                {
                    await Task.Delay(300);
                }
            }

            return false;
        }

        private static void DockerCleanUp(string containerId, string imageName, ServerJob job)
        {
            var finalState = ServerState.Stopped;

            try
            {
                var state = "";
                ProcessUtil.Run("docker", "inspect -f {{.State.Running}} " + containerId, throwOnError: false, outputDataReceived: text => state += text + "\n");

                // container is already stopped
                if (state.Contains("false"))
                {
                    var exitCode = "";
                    ProcessUtil.Run("docker", "inspect -f {{.State.ExitCode}} " + containerId, throwOnError: false, outputDataReceived: text => exitCode += text + "\n");

                    if (exitCode.Trim() != "0")
                    {
                        Log.WriteLine("Job failed");
                        job.Error = job.Output.ToString();
                        finalState = ServerState.Failed;
                    }
                }
                else
                {
                    ProcessUtil.Run("docker", $"stop {containerId}", throwOnError: false);
                }
            }
            finally
            {
                try
                {
                    ProcessUtil.Run("docker", $"rm --force {containerId}", throwOnError: false);

                    if (job.NoClean)
                    {
                        ProcessUtil.Run("docker", $"rmi --force --no-prune {imageName}", throwOnError: false);
                    }
                    else
                    {
                        ProcessUtil.Run("docker", $"rmi --force {imageName}", throwOnError: false);
                    }
                }
                catch (Exception e)
                {
                    Log.WriteLine("An error occurred while deleting the docker container: " + e.Message);
                    finalState = ServerState.Failed;
                }
                finally
                {
                    job.State = finalState;
                }
            }
        }

        private static async Task<string> CloneRestoreAndBuild(string path, ServerJob job, string dotnetHome, CancellationToken cancellationToken)
        {
            // Clone
            string benchmarkedDir = null;

            if (job.Source.SourceCode != null)
            {
                benchmarkedDir = "src";

                var src = Path.Combine(path, benchmarkedDir);
                var published = Path.Combine(src, "published");

                if (String.IsNullOrEmpty(job.Executable))
                {
                    Log.WriteLine($"Extracting files to {src}");
                    ZipFile.ExtractToDirectory(job.Source.SourceCode.TempFilename, src);
                }
                else
                {
                    Log.WriteLine($"Extracting files to {published}");
                    ZipFile.ExtractToDirectory(job.Source.SourceCode.TempFilename, published);
                }

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
                    var branchAndCommit = source.BranchOrCommit.Split('#', 2);

                    var dir = Git.Clone(path, source.Repository, shallow: branchAndCommit.Length == 1, branch: branchAndCommit[0], cancellationToken: cancellationToken);

                    var srcDir = Path.Combine(path, dir);

                    if (SourceRepoComparer.Instance.Equals(source, job.Source))
                    {
                        benchmarkedDir = dir;
                    }

                    if (branchAndCommit.Length > 1)
                    {
                        Git.Checkout(srcDir, branchAndCommit[1]);
                    }

                    if (source.InitSubmodules)
                    {
                        Git.InitSubModules(srcDir);
                    }
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            Debug.Assert(benchmarkedDir != null);

            // Computes the location of the benchmarked app
            var benchmarkedApp = Path.Combine(path, benchmarkedDir);

            if (!String.IsNullOrEmpty(job.Source.Project))
            {
                benchmarkedApp = Path.Combine(benchmarkedApp, Path.GetDirectoryName(FormatPathSeparators(job.Source.Project)));
            }

            Log.WriteLine($"Benchmarked Application in {benchmarkedApp}");

            var requireDotnetBuild =
                !String.IsNullOrEmpty(job.Source.Project) ||
                String.Equals("dotnet", job.Executable, StringComparison.OrdinalIgnoreCase)
                ;

            // Skip installing dotnet or building project if not necessary
            if (!requireDotnetBuild)
            {
                return benchmarkedDir;
            }

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

            // Define which Runtime and SDK will be installed.

            string targetFramework = DefaultTargetFramework;
            string channel = DefaultChannel;

            string runtimeVersion = job.RuntimeVersion;
            string desktopVersion = "";
            string aspNetCoreVersion = job.AspNetCoreVersion;
            string sdkVersion = job.SdkVersion;

            ConvertLegacyVersions(ref targetFramework, ref runtimeVersion, ref aspNetCoreVersion);

            // If a specific framework is set, use it instead of the detected one
            if (!String.IsNullOrEmpty(job.Framework))
            {
                targetFramework = job.Framework;
            }
            // If no version is set for runtime, check project's default tfm
            else if (!IsVersionPrefix(job.RuntimeVersion))
            {
                targetFramework = ResolveProjectTFM(job, benchmarkedApp, targetFramework);
            }

            PatchProjectFrameworkReference(job, benchmarkedApp);

            // If a specific channel is set, use it instead of the detected one
            if (!String.IsNullOrEmpty(job.Channel))
            {
                channel = job.Channel;
            }

            if (String.IsNullOrEmpty(runtimeVersion))
            {
                runtimeVersion = channel;
            }

            if (String.IsNullOrEmpty(desktopVersion))
            {
                desktopVersion = channel;
            }

            if (String.IsNullOrEmpty(aspNetCoreVersion))
            {
                aspNetCoreVersion = channel;
            }

            if (String.IsNullOrEmpty(sdkVersion))
            {
                sdkVersion = channel;
            }

            // Retrieve current versions
            var (currentRuntimeVersion, currentDesktopVersion, currentAspNetCoreVersion, currentSdkVersion) = await GetCurrentVersions(targetFramework);

            runtimeVersion = await ResolveRuntimeVersion(buildToolsPath, targetFramework, runtimeVersion, currentRuntimeVersion);

            sdkVersion = await ResolveSdkVersion(sdkVersion, currentSdkVersion);

            aspNetCoreVersion = await ResolveAspNetCoreVersion(aspNetCoreVersion, currentAspNetCoreVersion, targetFramework);

            sdkVersion = PatchOrCreateGlobalJson(job, benchmarkedApp, sdkVersion);

            var installAspNetSharedFramework = job.UseRuntimeStore || aspNetCoreVersion.StartsWith("3.0") || aspNetCoreVersion.StartsWith("3.1") || aspNetCoreVersion.StartsWith("5.0");

            var dotnetInstallStep = "";

            try
            {
                if (OperatingSystem == OperatingSystem.Windows)
                {
                    desktopVersion = ResolveDestopVersion(desktopVersion, currentDesktopVersion);

                    if (!_installedSdks.Contains(sdkVersion))
                    {
                        dotnetInstallStep = $"SDK '{sdkVersion}'";
                        Log.WriteLine($"Installing {dotnetInstallStep} ...");

                        // Install latest SDK version (and associated runtime)
                        ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; .\\dotnet-install.ps1 -Version {sdkVersion} -NoPath -SkipNonVersionedFiles -InstallDir {dotnetHome}",
                        log: false,
                        workingDirectory: _dotnetInstallPath,
                        environmentVariables: env));

                        _installedSdks.Add(sdkVersion);
                    }

                    if (!_installedDotnetRuntimes.Contains(runtimeVersion))
                    {
                        dotnetInstallStep = $"Runtime '{runtimeVersion}'";
                        Log.WriteLine($"Installing {dotnetInstallStep} ...");

                        // Install runtimes required for this scenario
                        ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; .\\dotnet-install.ps1 -Version {runtimeVersion} -Runtime dotnet -NoPath -SkipNonVersionedFiles -InstallDir {dotnetHome}",
                            log: false,
                            workingDirectory: _dotnetInstallPath,
                            environmentVariables: env,
                            cancellationToken: cancellationToken
                        ));

                        _installedDotnetRuntimes.Add(runtimeVersion);
                    }

                    try
                    {
                        // This is not required for < 3.0
                        var beforeDesktop = new[] { "netcoreapp2.1", "netcoreapp2.2", "netcoreapp3.0" };

                        if (!beforeDesktop.Contains(targetFramework))
                        {
                            if (!_installedDesktopRuntimes.Contains(desktopVersion) &&
                                !_ignoredDesktopRuntimes.Contains(desktopVersion))
                            {
                                dotnetInstallStep = $"Desktop runtime '{desktopVersion}'";
                                Log.WriteLine($"Installing {dotnetInstallStep} ...");

                                ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; .\\dotnet-install.ps1 -Version {desktopVersion} -Runtime windowsdesktop -NoPath -SkipNonVersionedFiles -InstallDir {dotnetHome}",
                                log: false,
                                workingDirectory: _dotnetInstallPath,
                                environmentVariables: env));

                                _installedDesktopRuntimes.Add(desktopVersion);
                            }
                            else
                            {
                                desktopVersion = SeekCompatibleDesktopRuntime(dotnetHome, targetFramework, desktopVersion);
                            }
                        }
                    }
                    catch
                    {
                        // Record that we don't need to try to download this version next time
                        _ignoredDesktopRuntimes.Add(desktopVersion);

                        // if the specified SDK can't be installed

                        // Seeking already installed Desktop runtimes
                        // c.f. https://github.com/dotnet/sdk/issues/4237

                        desktopVersion = SeekCompatibleDesktopRuntime(dotnetHome, targetFramework, desktopVersion);
                    }

                    // The aspnet core runtime is only available for >= 2.1, in 2.0 the dlls are contained in the runtime store
                    if (installAspNetSharedFramework && !_installedAspNetRuntimes.Contains(aspNetCoreVersion))
                    {
                        dotnetInstallStep = $"ASP.NET runtime '{aspNetCoreVersion}'";
                        Log.WriteLine($"Installing {dotnetInstallStep} ...");

                        // Install aspnet runtime required for this scenario
                        ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; .\\dotnet-install.ps1 -Version {aspNetCoreVersion} -Runtime aspnetcore -NoPath -SkipNonVersionedFiles -InstallDir {dotnetHome}",
                        log: false,
                        workingDirectory: _dotnetInstallPath,
                        environmentVariables: env));

                        _installedAspNetRuntimes.Add(aspNetCoreVersion);
                    }
                }
                else
                {
                    if (!_installedSdks.Contains(sdkVersion))
                    {
                        dotnetInstallStep = $"SDK '{sdkVersion}'";
                        Log.WriteLine($"Installing {dotnetInstallStep} ...");

                        // Install latest SDK version (and associated runtime)
                        ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --version {sdkVersion} --no-path --skip-non-versioned-files --install-dir {dotnetHome}",
                        log: false,
                        workingDirectory: _dotnetInstallPath,
                        environmentVariables: env));
                        _installedSdks.Add(sdkVersion);
                    }

                    if (!_installedDotnetRuntimes.Contains(runtimeVersion))
                    {
                        dotnetInstallStep = $"Runtime '{runtimeVersion}'";
                        Log.WriteLine($"Installing {dotnetInstallStep} ...");

                        // Install required runtime
                        ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --version {runtimeVersion} --runtime dotnet --no-path --skip-non-versioned-files --install-dir {dotnetHome}",
                        log: false,
                        workingDirectory: _dotnetInstallPath,
                        environmentVariables: env));

                        _installedDotnetRuntimes.Add(runtimeVersion);
                    }

                    // The aspnet core runtime is only available for >= 2.1, in 2.0 the dlls are contained in the runtime store
                    if (installAspNetSharedFramework && !_installedAspNetRuntimes.Contains(aspNetCoreVersion))
                    {
                        dotnetInstallStep = $"ASP.NET runtime '{aspNetCoreVersion}'";
                        Log.WriteLine($"Installing {dotnetInstallStep} ...");

                        // Install required runtime
                        ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --version {aspNetCoreVersion} --runtime aspnetcore --no-path --skip-non-versioned-files --install-dir {dotnetHome}",
                        log: false,
                        workingDirectory: _dotnetInstallPath,
                        environmentVariables: env));

                        _installedAspNetRuntimes.Add(aspNetCoreVersion);
                    }
                }
            }
            catch
            {
                job.Error = $"dotnet-install could not install a component: {dotnetInstallStep}";

                return null;
            }

            var dotnetDir = dotnetHome;

            // Updating ServerJob to reflect actual versions used
            job.AspNetCoreVersion = aspNetCoreVersion;
            job.RuntimeVersion = runtimeVersion;
            job.SdkVersion = sdkVersion;

            // Build and Restore
            var dotnetExecutable = GetDotNetExecutable(dotnetDir);

            var buildParameters =
                $"/p:MicrosoftNETCoreAppPackageVersion={runtimeVersion} " +
                $"/p:MicrosoftWindowsDesktopAppPackageVersion={desktopVersion} " +
                $"/p:MicrosoftAspNetCoreAppPackageVersion={aspNetCoreVersion} " +
                // The following properties could be removed in a future version
                $"/p:BenchmarksNETStandardImplicitPackageVersion={aspNetCoreVersion} " +
                $"/p:BenchmarksNETCoreAppImplicitPackageVersion={aspNetCoreVersion} " +
                $"/p:BenchmarksRuntimeFrameworkVersion={runtimeVersion} " +
                $"/p:BenchmarksTargetFramework={targetFramework} " +
                $"/p:BenchmarksAspNetCoreVersion={aspNetCoreVersion} " +
                $"/p:MicrosoftAspNetCoreAllPackageVersion={aspNetCoreVersion} " +
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
            else if (targetFramework == "netcoreapp3.1")
            {
                buildParameters += $"/p:MicrosoftNETCoreApp31PackageVersion={runtimeVersion} ";
                if (!job.UseRuntimeStore)
                {
                    buildParameters += $"/p:MicrosoftNETPlatformLibrary=Microsoft.NETCore.App ";
                }
            }
            else if (targetFramework == "netcoreapp5.0" || targetFramework == "net5.0")
            {
                buildParameters += $"/p:MicrosoftNETCoreApp50PackageVersion={runtimeVersion} ";
                buildParameters += $"/p:GenerateErrorForMissingTargetingPacks=false ";
                if (!job.UseRuntimeStore)
                {
                    buildParameters += $"/p:MicrosoftNETPlatformLibrary=Microsoft.NETCore.App ";
                }
            }
            else
            {
                job.Error = $"Unsupported framework: {targetFramework}";
                return null;
            }

            // #1445 force no cache for restore to avoid restore failures for packages published within last 30 minutes
            buildParameters += "/p:RestoreNoCache=true ";

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
                    if (job.Hardware == Hardware.ARM64)
                    {
                        buildParameters += "-r win-arm64 ";
                    }
                    else
                    {
                        buildParameters += "-r win-x64 ";
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    buildParameters += "-r osx-x64 ";
                }
                else
                {
                    if (job.Hardware == Hardware.ARM64)
                    {
                        buildParameters += "-r linux-arm64 ";
                    }
                    else
                    {
                        buildParameters += "-r linux-x64 ";
                    }
                }
            }

            // Copy build files before building/publishing
            foreach (var attachment in job.BuildAttachments)
            {
                var filename = Path.Combine(benchmarkedApp, attachment.Filename.Replace("\\", "/"));

                Log.WriteLine($"Creating build file: {filename}");

                if (File.Exists(filename))
                {
                    File.Delete(filename);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(filename));

                File.Copy(attachment.TempFilename, filename);
                File.Delete(attachment.TempFilename);
            }

            var outputFolder = Path.Combine(benchmarkedApp);

            if (String.IsNullOrEmpty(job.Executable))
            {
                outputFolder = Path.Combine(benchmarkedApp, "published");

                var projectFileName = Path.GetFileName(FormatPathSeparators(job.Source.Project));

                var arguments = $"publish {projectFileName} -c Release -o {outputFolder} {buildParameters}";

                Log.WriteLine($"Publishing application in {outputFolder} with: \n {arguments}");

                var stopwatch = new Stopwatch();
                stopwatch.Start();

                var buildResults = ProcessUtil.Run(dotnetExecutable, arguments,
                    workingDirectory: benchmarkedApp,
                    environmentVariables: env,
                    throwOnError: false,
                    outputDataReceived: text => job.BuildLog.AddLine(text)
                    );

                job.BuildLog.AddLine($"Command dotnet {arguments} returned exit code {buildResults.ExitCode}");

                if (buildResults.ExitCode != 0)
                {
                    job.Error = job.BuildLog.ToString();
                    return null;
                }

                stopwatch.Stop();

                job.BuildTime = stopwatch.Elapsed;

                job.Measurements.Enqueue(new Measurement
                {
                    Name = "benchmarks/build-time",
                    Timestamp = DateTime.UtcNow,
                    Value = stopwatch.ElapsedMilliseconds
                });

                Log.WriteLine($"Application published successfully in {job.BuildTime.TotalMilliseconds} ms");

                PatchRuntimeConfig(job, outputFolder, aspNetCoreVersion, runtimeVersion);
            }

            var publishedSize = DirSize(new DirectoryInfo(outputFolder)) / 1024;

            if (publishedSize != 0)
            {
                job.PublishedSize = publishedSize;

                job.Measurements.Enqueue(new Measurement
                {
                    Name = "benchmarks/published-size",
                    Timestamp = DateTime.UtcNow,
                    Value = publishedSize
                });
            }

            Log.WriteLine($"Published size: {job.PublishedSize}");

            // Copy crossgen in the app folder
            if (job.Collect && OperatingSystem == OperatingSystem.Linux)
            {
                // https://dotnetfeed.blob.core.windows.net/dotnet-core/flatcontainer/microsoft.netcore.app.runtime.linux-x64/index.json
                // This is because the package names were changed.For 3.0 +, look for ~/.nuget/packages/microsoft.netcore.app.runtime.linux-x64/<version>/tools/crossgen.

                Log.WriteLine("Copying crossgen to application folder");

                try
                {
                    // Downloading corresponding package
                    var runtimePath = Path.Combine(_rootTempDir, "RuntimePackages", $"microsoft.netcore.app.runtime.linux-x64.{runtimeVersion}.nupkg");

                    // Ensure the folder already exists
                    Directory.CreateDirectory(Path.GetDirectoryName(runtimePath));

                    if (!File.Exists(runtimePath))
                    {
                        Log.WriteLine($"Downloading runtime package");

                        var found = false;
                        foreach (var feed in _runtimeFeedUrls)
                        {
                            var url = $"{feed}/microsoft.netcore.app.runtime.linux-x64/{runtimeVersion}/microsoft.netcore.app.runtime.linux-x64.{runtimeVersion}.nupkg";

                            if (await DownloadFileAsync(url, runtimePath, maxRetries: 3, timeout: 60, throwOnError: false))
                            {
                                found = true;
                                break;
                            }
                            else
                            {
                                continue;

                            }
                        }

                        if (!found)
                        {
                            throw new Exception("Linux runtime package not found");
                        }
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
                catch (Exception e)
                {
                    Log.WriteLine("ERROR: Failed to download crossgen. " + e.ToString());
                }
            }

            // Download mono runtime
            if (!string.IsNullOrEmpty(job.UseMonoRuntime) && !string.Equals(job.UseMonoRuntime, "false", StringComparison.OrdinalIgnoreCase))
            {
                if (!job.SelfContained)
                {
                    throw new Exception("The job is trying to use the mono runtime but was not configured as self-contained.");
                }

                await UseMonoRuntimeAsync(runtimeVersion, outputFolder, job.UseMonoRuntime, job.Hardware);

                if (job.UseMonoRuntime.Equals("llvm-aot"))
                {
                    await AOT4Mono(sdkVersion, runtimeVersion, outputFolder);
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

                Directory.CreateDirectory(Path.GetDirectoryName(filename));

                File.Copy(attachment.TempFilename, filename);
                File.Delete(attachment.TempFilename);
            }

            return benchmarkedDir;

            long DirSize(DirectoryInfo d)
            {
                long size = 0;
                // Add file sizes.
                var fis = d.GetFiles();
                foreach (var fi in fis)
                {
                    size += fi.Length;
                }
                // Add subdirectory sizes.
                var dis = d.GetDirectories();
                foreach (var di in dis)
                {
                    size += DirSize(di);
                }
                return size;
            }
        }

        private static string ResolveProjectTFM(ServerJob job, string benchmarkedApp, string targetFramework)
        {
            var projectFileName = Path.Combine(benchmarkedApp, Path.GetFileName(FormatPathSeparators(job.Source.Project)));

            if (File.Exists(projectFileName))
            {
                var project = XDocument.Load(projectFileName);
                var targetFrameworkElement = project.Root
                    .Elements("PropertyGroup")
                    .Where(p => !p.Attributes("Condition").Any())
                    .SelectMany(x => x.Elements("TargetFramework"))
                    .FirstOrDefault();

                if (targetFrameworkElement != null)
                {
                    targetFramework = targetFrameworkElement.Value;

                    Log.WriteLine($"Detected target framework: '{targetFramework}'");
                }
                else
                {
                    var targetFrameworksElement = project.Root
                        .Elements("PropertyGroup")
                        .Where(p => !p.Attributes("Condition").Any())
                        .SelectMany(x => x.Elements("TargetFrameworks"))
                        .FirstOrDefault();

                    if (targetFrameworksElement != null)
                    {
                        targetFramework = targetFrameworksElement.Value.Split(';').FirstOrDefault();

                        Log.WriteLine($"Detected target framework: '{targetFramework}'");
                    }
                }
            }

            return targetFramework;
        }

        private static bool IsVersionPrefix(string version)
        {
            return !String.IsNullOrEmpty(version) && char.IsDigit(version[0]);
        }

        private static void PatchProjectFrameworkReference(ServerJob job, string benchmarkedApp)
        {
            var projectFileName = Path.Combine(benchmarkedApp, Path.GetFileName(FormatPathSeparators(job.Source.Project)));

            if (File.Exists(projectFileName))
            {
                Log.WriteLine("Patching project file with Framework References");

                var project = XDocument.Load(projectFileName);

                project.Root.Add(
                    new XElement("ItemGroup",
                        new XAttribute("Condition", "$(TargetFramework) == 'netcoreapp3.0' or $(TargetFramework) == 'netcoreapp3.1' or $(TargetFramework) == 'netcoreapp5.0' or $(TargetFramework) == 'net5.0'"),
                        new XElement("FrameworkReference",
                            new XAttribute("Update", "Microsoft.AspNetCore.App"),
                            new XAttribute("RuntimeFrameworkVersion", "$(MicrosoftAspNetCoreAppPackageVersion)")
                            ),
                        new XElement("FrameworkReference",
                            new XAttribute("Update", "Microsoft.NETCore.App"),
                            new XAttribute("RuntimeFrameworkVersion", "$(MicrosoftNETCoreAppPackageVersion)")
                            ),
                        new XElement("FrameworkReference",
                            new XAttribute("Update", "Microsoft.WindowsDesktop.App"),
                            new XAttribute("RuntimeFrameworkVersion", "$(MicrosoftWindowsDesktopAppPackageVersion)")
                            )
                    )
                );

                project.Save(projectFileName);
            }
        }

        private static void ConvertLegacyVersions(ref string targetFramework, ref string runtimeVersion, ref string aspNetCoreVersion)
        {
            // Converting legacy values

            if (runtimeVersion.EndsWith("*")) // 2.1.*, 2.*, 5.0.*
            {
                targetFramework = "netcoreapp" + runtimeVersion.Substring(0, 3);
                runtimeVersion = "edge";
            }

            if (runtimeVersion.Split('.').Length == 2) // 2.1, 5.0
            {
                targetFramework = "netcoreapp" + runtimeVersion.Substring(0, 3);
                runtimeVersion = "current";
            }

            if (aspNetCoreVersion.EndsWith("*")) // 2.1.*, 2.*
            {
                aspNetCoreVersion = "edge";
            }

            if (aspNetCoreVersion.Split('.').Length == 2) // 2.1, 5.0
            {
                aspNetCoreVersion = "current";
            }
        }

        private static string SeekCompatibleDesktopRuntime(string dotnetHome, string targetFramework, string desktopVersion)
        {
            var versionPrefix = targetFramework.Substring(targetFramework.Length - 3);

            foreach (var dir in Directory.GetDirectories(Path.Combine(dotnetHome, "shared", "Microsoft.WindowsDesktop.App")))
            {
                var version = new DirectoryInfo(dir).Name;
                _installedDesktopRuntimes.Add(version);

                // At least one matching Desktop runtime should be found as the sdk was installed
                // before
                if (version.StartsWith(versionPrefix))
                {
                    desktopVersion = version;
                }
            }

            return desktopVersion;
        }

        private static async Task<string> ResolveAspNetCoreVersion(string aspNetCoreVersion, string currentAspNetCoreVersion, string targetFramework)
        {
            var versionPrefix = targetFramework.Substring(targetFramework.Length - 3);

            // Define which ASP.NET Core packages version to use

            if (String.Equals(aspNetCoreVersion, "Current", StringComparison.OrdinalIgnoreCase))
            {
                aspNetCoreVersion = currentAspNetCoreVersion;
                Log.WriteLine($"ASP.NET: {aspNetCoreVersion} (Current)");
            }
            else if (String.Equals(aspNetCoreVersion, "Latest", StringComparison.OrdinalIgnoreCase))
            {
                // aspnet runtime service releases are not published on feeds
                if (versionPrefix != "5.0")
                {
                    aspNetCoreVersion = currentAspNetCoreVersion;
                    Log.WriteLine($"ASP.NET: {aspNetCoreVersion} (Latest - Fallback on Current)");
                }
                else
                {
                    aspNetCoreVersion = await GetFlatContainerVersion(_aspnetFlatContainerUrl, versionPrefix);
                    Log.WriteLine($"ASP.NET: {aspNetCoreVersion} (Latest)");
                }
            }
            else if (String.Equals(aspNetCoreVersion, "Edge", StringComparison.OrdinalIgnoreCase))
            {
                // aspnet runtime service releases are not published on feeds
                if (versionPrefix != "5.0")
                {
                    aspNetCoreVersion = currentAspNetCoreVersion;
                    Log.WriteLine($"ASP.NET: {aspNetCoreVersion} (Edge - Fallback on Current)");
                }
                else
                {
                    aspNetCoreVersion = await GetFlatContainerVersion(_aspnetFlatContainerUrl, versionPrefix);
                    Log.WriteLine($"ASP.NET: {aspNetCoreVersion} (Latest)");
                }
            }
            else
            {
                Log.WriteLine($"ASP.NET: {aspNetCoreVersion} (Specific)");
            }

            return aspNetCoreVersion;
        }

        private static string PatchOrCreateGlobalJson(ServerJob job, string benchmarkedApp, string sdkVersion)
        {
            // Looking for the first existing global.json file to update

            var globalJsonPath = new DirectoryInfo(benchmarkedApp);

            while (globalJsonPath != null && !File.Exists(Path.Combine(globalJsonPath.FullName, "global.json")) && globalJsonPath != null)
            {
                globalJsonPath = globalJsonPath.Parent;
            }

            globalJsonPath = globalJsonPath ?? new DirectoryInfo(benchmarkedApp);

            var globalJsonFilename = Path.Combine(globalJsonPath.FullName, "global.json");

            if (job.NoGlobalJson)
            {
                if (!File.Exists(globalJsonFilename))
                {
                    Log.WriteLine($"Could not find global.json file");
                }
                else
                {
                    Log.WriteLine($"Searching SDK version in global.json");

                    var globalObject = JObject.Parse(File.ReadAllText(globalJsonFilename));
                    sdkVersion = globalObject["sdk"]["version"].ToString();

                    Log.WriteLine($"Detecting global.json SDK version: {sdkVersion}");
                }
            }
            else
            {
                if (!File.Exists(globalJsonFilename))
                {
                    // No global.json found
                    Log.WriteLine($"Creating custom global.json");

                    var globalJson = "{ \"sdk\": { \"version\": \"" + sdkVersion + "\" } }";
                    File.WriteAllText(Path.Combine(benchmarkedApp, "global.json"), globalJson);
                }
                else
                {
                    // File found, we need to update it
                    Log.WriteLine($"Patching existing global.json file");

                    var globalObject = JObject.Parse(File.ReadAllText(globalJsonFilename));

                    // Create the "sdk" property if it doesn't exist
                    globalObject.TryAdd("sdk", new JObject());

                    globalObject["sdk"]["version"] = new JValue(sdkVersion);

                    File.WriteAllText(globalJsonFilename, globalObject.ToString());
                }
            }

            return sdkVersion;
        }

        private static void PatchRuntimeConfig(ServerJob job, string publishFolder, string aspnetcoreversion, string runtimeversion)
        {
            var folder = new DirectoryInfo(publishFolder);
            var runtimeConfigFilename = folder.GetFiles("*.runtimeconfig.json").FirstOrDefault()?.FullName;

            if (!File.Exists(runtimeConfigFilename))
            {
                Log.WriteLine("Ignoring runtimeconfig.json. File not found.");
                return;
            }

            // File found, we need to update it
            Log.WriteLine($"Patching {Path.GetFileName(runtimeConfigFilename)} ");

            var runtimeObject = JObject.Parse(File.ReadAllText(runtimeConfigFilename));

            var runtimeOptions = runtimeObject["runtimeOptions"] as JObject;

            if (runtimeOptions.ContainsKey("includedFrameworks"))
            {
                Log.WriteLine("Application is self-contained, skipping runtimconfig");
                return;
            }

            // Remove exising "framework" (singular) node
            runtimeOptions.Remove("framework");

            // Create the "frameworks" property instead
            var frameworks = new JArray();
            runtimeOptions.TryAdd("frameworks", frameworks);

            frameworks.Add(
                    new JObject(
                        new JProperty("name", "Microsoft.NETCore.App"),
                        new JProperty("version", runtimeversion)
                    ));
            frameworks.Add(
                    new JObject(
                        new JProperty("name", "Microsoft.AspNetCore.App"),
                        new JProperty("version", aspnetcoreversion)
                    ));

            File.WriteAllText(runtimeConfigFilename, runtimeObject.ToString());
        }

        private static async Task<string> ResolveSdkVersion(string sdkVersion, string currentSdkVersion)
        {
            if (String.Equals(sdkVersion, "Current", StringComparison.OrdinalIgnoreCase))
            {
                sdkVersion = currentSdkVersion;
                Log.WriteLine($"SDK: {sdkVersion} (Current)");
            }
            else if (String.Equals(sdkVersion, "Latest", StringComparison.OrdinalIgnoreCase))
            {
                sdkVersion = await GetAspNetSdkVersion();
                Log.WriteLine($"SDK: {sdkVersion} (Latest)");
            }
            else if (String.Equals(sdkVersion, "Edge", StringComparison.OrdinalIgnoreCase))
            {
                sdkVersion = await ParseLatestVersionFile(_latestSdkVersionUrl);
                Log.WriteLine($"SDK: {sdkVersion} (Edge)");
            }
            else
            {
                Log.WriteLine($"SDK: {sdkVersion} (Specific)");
            }

            return sdkVersion;
        }

        private static async Task<string> ResolveRuntimeVersion(string buildToolsPath, string targetFramework, string runtimeVersion, string currentRuntimeVersion)
        {
            if (String.Equals(runtimeVersion, "Current", StringComparison.OrdinalIgnoreCase))
            {
                runtimeVersion = currentRuntimeVersion;
                Log.WriteLine($"Runtime: {runtimeVersion} (Current)");
            }
            else if (String.Equals(runtimeVersion, "Latest", StringComparison.OrdinalIgnoreCase))
            {
                // Get the version that is defined by the ASP.NET repository
                // Note: to use the latest build available, use Edge channel
                runtimeVersion = await GetAspNetRuntimeVersion(buildToolsPath, targetFramework);
                Log.WriteLine($"Runtime: {runtimeVersion} (Latest)");
            }
            else if (String.Equals(runtimeVersion, "Edge", StringComparison.OrdinalIgnoreCase))
            {
                // Older versions are still published on old feed. Including service releases

                foreach (var runtimeApiUrl in _runtimeFeedUrls)
                {
                    try
                    {
                        runtimeVersion = await GetFlatContainerVersion(runtimeApiUrl + "/microsoft.netcore.app.runtime.win-x64/index.json", targetFramework.Substring(targetFramework.Length - 3));

                        if (!String.IsNullOrEmpty(runtimeVersion))
                        {
                            Log.WriteLine($"Runtime: {runtimeVersion} (Edge)");
                            break;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            else
            {
                // Custom version
                Log.WriteLine($"Runtime: {runtimeVersion} (Specific)");
            }

            return runtimeVersion;
        }

        private static string ResolveDestopVersion(string desktopVersion, string currentDesktopVersion)
        {
            if (String.Equals(desktopVersion, "Current", StringComparison.OrdinalIgnoreCase))
            {
                desktopVersion = currentDesktopVersion;
                Log.WriteLine($"Desktop: {desktopVersion} (Current)");
            }
            else if (String.Equals(desktopVersion, "Latest", StringComparison.OrdinalIgnoreCase))
            {
                desktopVersion = currentDesktopVersion;
                Log.WriteLine($"Desktop: {currentDesktopVersion} (Latest)");
            }
            else if (String.Equals(desktopVersion, "Edge", StringComparison.OrdinalIgnoreCase))
            {
                desktopVersion = currentDesktopVersion;
                Log.WriteLine($"Desktop: {currentDesktopVersion} (Edge)");
            }
            else
            {
                // Custom version
                desktopVersion = currentDesktopVersion;
                Log.WriteLine($"Desktop: {desktopVersion} (Current)");
            }

            return desktopVersion;
        }

        public static async Task<string> GetAspNetSdkVersion()
        {
            var globalJson = await DownloadContentAsync(_aspnetSdkVersionUrl, maxRetries: 5, timeout: 10);
            var globalObject = JObject.Parse(globalJson);
            return globalObject["sdk"]["version"].ToString();
        }

        /// <summary>
        /// Retrieves the runtime version used on ASP.NET Coherence builds
        /// </summary>
        private static async Task<string> GetAspNetRuntimeVersion(string buildToolsPath, string targetFramework)
        {
            var aspNetCoreDependenciesPath = Path.Combine(buildToolsPath, Path.GetFileName(_aspNetCoreDependenciesUrl));

            string latestRuntimeVersion = "";

            switch (targetFramework)
            {
                case "netcoreapp2.1":

                    await DownloadFileAsync(String.Format(_aspNetCoreDependenciesUrl, "release/2.1/build/dependencies.props"), aspNetCoreDependenciesPath, maxRetries: 5, timeout: 10);
                    latestRuntimeVersion = XDocument.Load(aspNetCoreDependenciesPath).Root
                        .Elements("PropertyGroup")
                        .Select(x => x.Element("MicrosoftNETCoreAppPackageVersion"))
                        .Where(x => x != null)
                        .FirstOrDefault()
                        .Value;

                    break;

                case "netcoreapp2.2":

                    await DownloadFileAsync(String.Format(_aspNetCoreDependenciesUrl, "release/2.2/build/dependencies.props"), aspNetCoreDependenciesPath, maxRetries: 5, timeout: 10);
                    latestRuntimeVersion = XDocument.Load(aspNetCoreDependenciesPath).Root
                        .Elements("PropertyGroup")
                        .Select(x => x.Element("MicrosoftNETCoreAppPackageVersion"))
                        .Where(x => x != null)
                        .FirstOrDefault()
                        .Value;

                    break;

                case "netcoreapp3.0":

                    await DownloadFileAsync(String.Format(_aspNetCoreDependenciesUrl, "release/3.0/eng/Versions.props"), aspNetCoreDependenciesPath, maxRetries: 5, timeout: 10);
                    latestRuntimeVersion = XDocument.Load(aspNetCoreDependenciesPath).Root
                        .Elements("PropertyGroup")
                        .Select(x => x.Element("MicrosoftNETCoreAppRefPackageVersion"))
                        .Where(x => x != null)
                        .FirstOrDefault()
                        .Value;

                    break;

                case "netcoreapp3.1":

                    await DownloadFileAsync(String.Format(_aspNetCoreDependenciesUrl, "release/3.1/eng/Versions.props"), aspNetCoreDependenciesPath, maxRetries: 5, timeout: 10);
                    latestRuntimeVersion = XDocument.Load(aspNetCoreDependenciesPath).Root
                        .Elements("PropertyGroup")
                        .Select(x => x.Element("MicrosoftNETCoreAppRuntimewinx64PackageVersion"))
                        .Where(x => x != null)
                        .FirstOrDefault()
                        .Value;

                    break;

                case "netcoreapp5.0":
                case "net5.0":

                    await DownloadFileAsync(String.Format(_aspNetCoreDependenciesUrl, "master/eng/Versions.props"), aspNetCoreDependenciesPath, maxRetries: 5, timeout: 10);
                    latestRuntimeVersion = XDocument.Load(aspNetCoreDependenciesPath).Root
                        .Elements("PropertyGroup")
                        .Select(x => x.Element("MicrosoftNETCoreAppRuntimewinx64PackageVersion"))
                        .Where(x => x != null)
                        .FirstOrDefault()
                        .Value;

                    break;
            }

            Log.WriteLine($"Detecting AspNetCore repository runtime version: {latestRuntimeVersion}");
            return latestRuntimeVersion;
        }

        /// <summary>
        /// Retrieves the Current runtime and sdk versions for a tfm
        /// </summary>
        private static async Task<(string Runtime, string Desktop, string AspNet, string Sdk)> GetCurrentVersions(string targetFramework)
        {
            var frameworkVersion = targetFramework.Substring(targetFramework.Length - 3); // 3.1
            var metadataUrl = $"https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/{frameworkVersion}/releases.json";

            var content = await DownloadContentAsync(metadataUrl);
            var index = JObject.Parse(content);

            var aspnet = index.SelectToken($"$.releases[0].aspnetcore-runtime.version").ToString();
            var sdk = index.SelectToken($"$.releases[0].sdk.version").ToString();
            var runtime = index.SelectToken($"$.releases[0].runtime.version").ToString();
            var desktop = index.SelectToken($"$.releases[0].windowsdesktop.version").ToString();

            return (runtime, desktop, aspnet, sdk);
        }

        /// <summary>
        /// Parses files that contain two lines: a sha and a version
        /// </summary>
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

        private static async Task<bool> DownloadFileAsync(string url, string outputPath, int maxRetries, int timeout = 5, bool throwOnError = true)
        {
            Log.WriteLine($"Downloading {url}");

            HttpResponseMessage response = null;

            for (var i = 0; i < maxRetries; ++i)
            {
                try
                {
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
                    response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);
                    response.EnsureSuccessStatusCode();

                    // This probably won't use async IO on windows since the stream
                    // needs to created with the right flags
                    using (var stream = File.Create(outputPath))
                    {
                        // Copy the response stream directly to the file stream
                        await response.Content.CopyToAsync(stream);
                    }

                    return true;
                }
                catch (OperationCanceledException)
                {
                    Log.WriteLine($"Timeout trying to download {url}, attempt {i + 1}");
                }
                catch (HttpRequestException)
                {
                    // No need to retry on a 404
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Failed to download {url}, attempt {i + 1}, Exception: {ex}");
                }
            }

            if (throwOnError)
            {
                throw new InvalidOperationException($"Failed to download {url} after {maxRetries} attempts");
            }

            return false;
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

        private static void TryDeleteDir(string path, bool rethrow = true)
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
                catch
                {
                    Log.WriteLine("Error, retrying ...");

                    if (i < 9)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                    }
                    else
                    {
                        Log.WriteLine("All retries failed");

                        if (rethrow)
                        {
                            throw;
                        }
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

        private static async Task<Process> StartProcess(string hostname, string benchmarksRepo, ServerJob job, string dotnetHome)
        {
            var workingDirectory = !String.IsNullOrEmpty(job.Source.Project)
                ? Path.Combine(benchmarksRepo, Path.GetDirectoryName(FormatPathSeparators(job.Source.Project)))
                : benchmarksRepo
                ;

            var scheme = (job.Scheme == Scheme.H2 || job.Scheme == Scheme.Https) ? "https" : "http";
            var serverUrl = $"{scheme}://{hostname}:{job.Port}";
            var executable = GetDotNetExecutable(dotnetHome);
            var projectFilename = Path.GetFileNameWithoutExtension(FormatPathSeparators(job.Source.Project));

            var benchmarksDll = !String.IsNullOrEmpty(projectFilename)
                ? Path.Combine(workingDirectory, "published", $"{projectFilename}.dll")
                : Path.Combine(workingDirectory, "published")
                ;

            var iis = job.WebHost == WebHost.IISInProcess || job.WebHost == WebHost.IISOutOfProcess;

            // Running BeforeScript
            if (!String.IsNullOrEmpty(job.BeforeScript))
            {
                var segments = job.BeforeScript.Split(' ', 2);
                var result = ProcessUtil.Run(segments[0], segments.Length > 1 ? segments[1] : "", workingDirectory: workingDirectory, log: true, outputDataReceived: text => job.Output.AddLine(text));
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

            if (!String.IsNullOrEmpty(job.Executable))
            {
                executable = job.Executable;

                if (String.Equals(executable, "dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    executable = GetDotNetExecutable(dotnetHome);
                }
                else
                {
                    // we need the full path to run this, as it is not in the path
                    executable = Path.Combine(workingDirectory, executable);
                }
            }

            job.BasePath = workingDirectory;

            var arguments = $" --nonInteractive true" +
                    $" --scenarios {job.Scenario}";

            switch (job.WebHost)
            {
                case WebHost.HttpSys:
                    arguments += " --server HttpSys";
                    break;
                case WebHost.KestrelSockets:
                    arguments += " --server Kestrel --kestrelTransport Sockets";
                    break;
                case WebHost.KestrelLibuv:
                    arguments += " --server Kestrel --kestrelTransport Libuv";
                    break;
                case WebHost.IISInProcess:
                    arguments += " --server IISInProcess";
                    break;
                case WebHost.IISOutOfProcess:
                    arguments += " --server IISOutOfProcess";
                    break;
                case WebHost.CCore:
                    arguments += " --server CCore";
                    break;
                default:
                    arguments += $" --server {job.WebHost}";
                    break;

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

            // Benchmarkdotnet needs the actual cli path to generate its benchmarked app
            commandLine = commandLine.Replace("{{benchmarks-cli}}", executable);

            if (iis)
            {
                Log.WriteLine($"Generating application host config for '{executable} {commandLine}'");

                var apphost = GenerateApplicationHostConfig(job, job.BasePath, executable, commandLine, hostname);
                commandLine = $"-h \"{apphost}\"";
                executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\inetsrv\w3wp.exe");
            }

            // The cgroup limits are set on the root group as .NET is reading these only, and not the ones that it would run inside

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && (job.MemoryLimitInBytes > 0 || job.CpuLimitRatio > 0 || !String.IsNullOrEmpty(job.CpuSet)))
            {
                var controller = GetCGroupController(job);

                var cgcreate = ProcessUtil.Run("cgcreate", $"-g memory,cpu,cpuset:{controller}", log: true);

                if (cgcreate.ExitCode > 0)
                {
                    job.Error += "Could not create cgroup";
                    return null;
                }

                if (job.MemoryLimitInBytes > 0)
                {
                    ProcessUtil.Run("cgset", $"-r memory.limit_in_bytes={job.MemoryLimitInBytes} {controller}", log: true);
                }
                else
                {
                    ProcessUtil.Run("cgset", $"-r memory.limit_in_bytes=-1 {controller}", log: true);
                }

                if (job.CpuLimitRatio > 0)
                {
                    // Ensure the cfs_period_us is the same as what docker would use
                    ProcessUtil.Run("cgset", $"-r cpu.cfs_period_us={_defaultDockerCfsPeriod} {controller}", log: true);
                    ProcessUtil.Run("cgset", $"-r cpu.cfs_quota_us={Math.Floor(job.CpuLimitRatio * _defaultDockerCfsPeriod)} {controller}", log: true);
                }
                else
                {
                    ProcessUtil.Run("cgset", $"-r cpu.cfs_quota_us=-1 {controller}", log: true);
                }


                if (!String.IsNullOrEmpty(job.CpuSet))
                {

                    ProcessUtil.Run("cgset", $"-r cpuset.cpus={job.CpuSet} {controller}", log: true);
                }
                else
                {
                    ProcessUtil.Run("cgset", $"-r cpuset.cpus=0-{Environment.ProcessorCount - 1} {controller}", log: true);
                }

                // The cpuset.mems value for the 'benchmarks' controller needs to match the root one
                // to be compatible with the allowed nodes
                var memsRoot = File.ReadAllText("/sys/fs/cgroup/cpuset/cpuset.mems");

                // Both cpus and mems need to be initialized
                ProcessUtil.Run("cgset", $"-r cpuset.mems={memsRoot} {controller}", log: true);

                commandLine = $"-g memory,cpu,cpuset:{controller} {executable} {commandLine}";
                executable = "cgexec";
            }

            Log.WriteLine($"Invoking executable: {executable}");
            Log.WriteLine($"  Arguments: {commandLine}");
            Log.WriteLine($"  Working directory: {workingDirectory}");

            var process = new Process()
            {
                StartInfo = {
                    FileName = executable,
                    Arguments = commandLine,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,

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

            foreach (var env in job.EnvironmentVariables)
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

                    job.Output.AddLine(e.Data);

                    if (job.State == ServerState.Starting &&
                        ((!String.IsNullOrEmpty(job.ReadyStateText) && e.Data.IndexOf(job.ReadyStateText, StringComparison.OrdinalIgnoreCase) >= 0) || job.IsConsoleApp))
                    {
                        RunAndTrace();
                    }

                    ParseMeasurementOutput(job, job.Output);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e != null && e.Data != null)
                {
                    var log = "[STDERR] " + e.Data;

                    Log.WriteLine(log);

                    job.Output.AddLine(log);

                    if (job.State == ServerState.Starting &&
                        ((!String.IsNullOrEmpty(job.ReadyStateText) && e.Data.IndexOf(job.ReadyStateText, StringComparison.OrdinalIgnoreCase) >= 0) || job.IsConsoleApp))
                    {
                        MarkAsRunning(hostname, job, stopwatch);

                        if (!job.CollectStartup)
                        {
                            if (job.Collect)
                            {
                                StartCollection(Path.Combine(benchmarksRepo, job.BasePath), job);
                            }

                            if (job.DotNetTrace)
                            {
                                StartDotNetTrace(process.Id, job);
                            }
                        }
                    }

                    // Detect the app is wrapping a child process
                    var processIdMarker = "##ChildProcessId:";
                    if (e.Data.StartsWith(processIdMarker) 
                        && int.TryParse(e.Data.Substring(processIdMarker.Length), out var childProcessId))
                    {
                        Log.WriteLine($"Tracking child process id: {childProcessId}");
                        job.ChildProcessId = childProcessId;
                    }
                }
            };

            if (job.CollectStartup)
            {
                if (job.Collect)
                {
                    StartCollection(Path.Combine(benchmarksRepo, job.BasePath), job);
                }

                if (job.DotNetTrace)
                {
                    StartDotNetTrace(process.Id, job);
                }
            }

            stopwatch.Start();
            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // A Console App that has no ReadyStateText should be assumed as started
            if (String.IsNullOrEmpty(job.ReadyStateText) && job.IsConsoleApp)
            {
                RunAndTrace();
            }

            if (job.CollectCounters)
            {
                StartCounters(job);
            }

            StartMeasurement(job);

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

            // We try to detect an endpoint is ready if we are running in IIS (no console logs)
            // or if no ReadyStateText is provided and the application is not a ConsoleApp
            if (iis || (String.IsNullOrEmpty(job.ReadyStateText) && !job.IsConsoleApp))
            {
                await WaitToListen(job, hostname);

                RunAndTrace();
            }

            return process;

            void RunAndTrace()
            {
                if (MarkAsRunning(hostname, job, stopwatch))
                {
                    if (!job.CollectStartup)
                    {
                        if (job.Collect)
                        {
                            StartCollection(Path.Combine(benchmarksRepo, job.BasePath), job);
                        }

                        if (job.DotNetTrace)
                        {
                            StartDotNetTrace(process.Id, job);
                        }
                    }
                }
            }
        }

        private static string GetCGroupController(ServerJob job)
        {
            // Create a unique cgroup controller per agent
            return $"benchmarks-{Process.GetCurrentProcess().Id}-{job.Id}";
        }

        private static readonly Dictionary<string, string> MetadataProviderPrefixes = new Dictionary<string, string>()
        {
            ["System.Runtime"] = "runtime-counter",
            ["Microsoft-AspNetCore-Server-Kestrel"] = "kestrel-counter",
            ["Microsoft.AspNetCore.Hosting"] = "aspnet-counter",
            ["Microsoft.AspNetCore.Http.Connections"] = "signalr-counter",
            ["Grpc.AspNetCore.Server"] = "grpc-server-counter",
            ["Grpc.Net.client"] = "grpc-client-counter",
            ["Npgsql"] = "npgsql-counter"
        };

        private static readonly Dictionary<string, MeasurementMetadata[]> MetadataProviders = new Dictionary<string, MeasurementMetadata[]>
        {
            ["System.Runtime"] = new MeasurementMetadata[]
            {
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/cpu-usage", LongDescription = "Amount of time the process has utilized the CPU (ms)", ShortDescription = "CPU Usage (%)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/working-set", LongDescription = "Amount of working set used by the process (MB)", ShortDescription = "Working Set (MB)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/gc-heap-size", LongDescription = "Total heap size reported by the GC (MB)", ShortDescription = "GC Heap Size (MB)", Format = "n0", Aggregate = Operation.Median, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/gen-0-gc-count", LongDescription = "Number of Gen 0 GCs / sec", ShortDescription = "Gen 0 GC (#/s)", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/gen-1-gc-count", LongDescription = "Number of Gen 1 GCs / sec", ShortDescription = "Gen 1 GC (#/s)", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/gen-2-gc-count", LongDescription = "Number of Gen 2 GCs / sec", ShortDescription = "Gen 2 GC (#/s)", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/time-in-gc", LongDescription = "% time in GC since the last GC", ShortDescription = "Time in GC (%)", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/gen-0-size", LongDescription = "Gen 0 Heap Size", ShortDescription = "Gen 0 Size (B)", Format = "n0", Aggregate = Operation.Median, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/gen-1-size", LongDescription = "Gen 1 Heap Size", ShortDescription = "Gen 1 Size (B)", Format = "n0", Aggregate = Operation.Median, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/gen-2-size", LongDescription = "Gen 2 Heap Size", ShortDescription = "Gen 2 Size (B)", Format = "n0", Aggregate = Operation.Median, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/loh-size", LongDescription = "LOH Heap Size", ShortDescription = "LOH Size (B)", Format = "n0", Aggregate = Operation.Median, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/alloc-rate", LongDescription = "Allocation Rate", ShortDescription = "Allocation Rate (B/sec)", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/assembly-count", LongDescription = "Number of Assemblies Loaded", ShortDescription = "# of Assemblies Loaded", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/exception-count", LongDescription = "Number of Exceptions / sec", ShortDescription = "Exceptions (#/s)", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/threadpool-thread-count", LongDescription = "Number of ThreadPool Threads", ShortDescription = "ThreadPool Threads Count", Format = "n0", Aggregate = Operation.Median, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/monitor-lock-contention-count", LongDescription = "Monitor Lock Contention Count", ShortDescription = "Lock Contention (#/s)", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/threadpool-queue-length", LongDescription = "ThreadPool Work Items Queue Length", ShortDescription = "ThreadPool Queue Length", Format = "n0", Aggregate = Operation.Median, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "System.Runtime", Name = "runtime-counter/threadpool-completed-items-count", LongDescription = "ThreadPool Completed Work Items Count", ShortDescription = "ThreadPool Items (#/s)", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
            },
            ["Microsoft-AspNetCore-Server-Kestrel"] = new MeasurementMetadata[]
            {
                new MeasurementMetadata { Source = "Kestrel", Name = "kestrel-counter/connections-per-second", LongDescription = "Connection Rate", ShortDescription = "Connections/s (max)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Kestrel", Name = "kestrel-counter/total-connections", LongDescription = "Total Connections", ShortDescription = "Connections", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Kestrel", Name = "kestrel-counter/tls-handshakes-per-second", LongDescription = "TLS Handshake Rate", ShortDescription = "TLS Handshakes/sec (max)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Kestrel", Name = "kestrel-counter/total-tls-handshakes", LongDescription = "Total TLS Handshakes", ShortDescription = "TLS Handshakes", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Kestrel", Name = "kestrel-counter/current-tls-handshakes", LongDescription = "Current TLS Handshakes", ShortDescription = "TLS Handshakes (max)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Kestrel", Name = "kestrel-counter/failed-tls-handshakes", LongDescription = "Failed TLS Handshakes", ShortDescription = "Failed TLS Handshakes (max)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Kestrel", Name = "kestrel-counter/current-connections", LongDescription = "Current Connections", ShortDescription = "Concurrent Connections (max)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Kestrel", Name = "kestrel-counter/connection-queue-length", LongDescription = "Connection Queue Length", ShortDescription = "Connection Queue Length (max)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Kestrel", Name = "kestrel-counter/request-queue-length", LongDescription = "Request Queue Length", ShortDescription = "Request Queue Length (max)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Kestrel", Name = "kestrel-counter/current-upgraded-requests", LongDescription = "Current Upgraded Requests (WebSockets)", ShortDescription = "Upgraded Requests (max)", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
            },
            ["Microsoft.AspNetCore.Hosting"] = new MeasurementMetadata[]
            {
                new MeasurementMetadata { Source = "ASP.NET Core", Name = "aspnet-counter/requests-per-second", LongDescription = "Maximum Requests Per Second", ShortDescription = "Requests/sec (max)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "ASP.NET Core", Name = "aspnet-counter/total-requests", LongDescription = "Total Requests", ShortDescription = "Requests", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "ASP.NET Core", Name = "aspnet-counter/current-requests", LongDescription = "Maximum Current Requests", ShortDescription = "Concurrent Requests (max)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "ASP.NET Core", Name = "aspnet-counter/failed-requests", LongDescription = "Failed Requests", ShortDescription = "Failed Requests", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
            },
            ["Microsoft.AspNetCore.Http.Connections"] = new MeasurementMetadata[]
            {
                new MeasurementMetadata { Source = "SignalR", Name = "signalr-counter/connections-started", LongDescription = "Total Connections Started", ShortDescription = "Connections Started", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "SignalR", Name = "signalr-counter/connections-stopped", LongDescription = "Total Connections Stopped", ShortDescription = "Connections Stopped", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "SignalR", Name = "signalr-counter/connections-timed-out", LongDescription = "Total Connections Timed Out", ShortDescription = "Timed Out Connections", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "SignalR", Name = "signalr-counter/current-connections", LongDescription = "Maximum of Current Connections", ShortDescription = "Concurrent Connections (max)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "SignalR", Name = "signalr-counter/connections-duration", LongDescription = "Average Connection Duration (ms)", ShortDescription = "Connection Duration (avg, ms)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
            },
            ["Grpc.AspNetCore.Server"] = new MeasurementMetadata[]
            {
                new MeasurementMetadata { Source = "gRPC Server", Name = "grpc-server-counter/total-calls", LongDescription = "Total Calls", ShortDescription = "Calls", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "gRPC Server", Name = "grpc-server-counter/current-calls", LongDescription = "Current Calls", ShortDescription = "Concurrent Calls (max)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "gRPC Server", Name = "grpc-server-counter/calls-failed", LongDescription = "Total Calls Failed", ShortDescription = "Failed Calls", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "gRPC Server", Name = "grpc-server-counter/calls-deadline-exceeded", LongDescription = "Total Calls Deadline Exceeded", ShortDescription = "Deadline Exceeded Calls", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "gRPC Server", Name = "grpc-server-counter/messages-sent", LongDescription = "Total Messages Sent", ShortDescription = "Messages Sent", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "gRPC Server", Name = "grpc-server-counter/messages-received", LongDescription = "Total Messages Received", ShortDescription = "Messages Received", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "gRPC Server", Name = "grpc-server-counter/calls-unimplemented", LongDescription = "Total Calls Unimplemented", ShortDescription = "Unimplemented Calls", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
            },
            ["Grpc.Net.Client"] = new MeasurementMetadata[]
            {
                new MeasurementMetadata { Source = "gRPC Client", Name = "grpc-client-counter/total-calls", LongDescription = "Total Calls", ShortDescription = "Calls", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "gRPC Client", Name = "grpc-client-counter/current-calls", LongDescription = "Current Calls", ShortDescription = "Concurrent Calls (max)", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "gRPC Client", Name = "grpc-client-counter/calls-failed", LongDescription = "Total Calls Failed", ShortDescription = "Failed Calls", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "gRPC Client", Name = "grpc-client-counter/calls-deadline-exceeded", LongDescription = "Total Calls Deadline Exceeded", ShortDescription = "Total Calls Deadline Exceeded", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "gRPC Client", Name = "grpc-client-counter/messages-sent", LongDescription = "Total Messages Sent", ShortDescription = "Messages Sent", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "gRPC Client", Name = "grpc-client-counter/messages-received", LongDescription = "Total Messages Received", ShortDescription = "Messages Received", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
            },
            ["Npgsql"] = new MeasurementMetadata[]
            {
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/bytes-written-per-second", LongDescription = "Bytes Written", ShortDescription = "Bytes Written", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/bytes-read-per-second", LongDescription = "Bytes Read", ShortDescription = "Bytes Read", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/commands-per-second", LongDescription = "Command Rate", ShortDescription = "Command Rate", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/total-commands", LongDescription = "Total Commands", ShortDescription = "Total Commands", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/current-commands", LongDescription = "Current Commands", ShortDescription = "Current Commands", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/failed-commands", LongDescription = "Failed Commands", ShortDescription = "Failed Commands", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/prepared-commands-ratio", LongDescription = "Prepared Commands Ratio", ShortDescription = "Prepared Commands Ratio", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/connection-pools", LongDescription = "Connection Pools", ShortDescription = "Connection Pools", Format = "n0", Aggregate = Operation.Max, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/idle-connections", LongDescription = "Idle Connections", ShortDescription = "Idle Connections", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/busy-connections", LongDescription = "Busy Connections", ShortDescription = "Busy Connections", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/multiplexing-average-commands-per-batch", LongDescription = "Average commands per multiplexing batch", ShortDescription = "Commands per multiplexing batch", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/multiplexing-average-waits-per-batch", LongDescription = "Average waits per multiplexing batch", ShortDescription = "Waits per multiplexing batch", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
                new MeasurementMetadata { Source = "Npgsql", Name = "npgsql-counter/multiplexing-average-write-time-per-batch", LongDescription = "Average write time per multiplexing batch (us)", ShortDescription = "Time per multiplexing batch (us)", Format = "n0", Aggregate = Operation.Avg, Reduce = Operation.Max },
            }
        };

        private static void StartCounters(ServerJob job)
        {
            eventPipeTerminated = false;
            eventPipeTask = new Task(async () =>
            {
                // If not specific provider was defined, add System.Runtime
                if (job.CounterProviders.Count == 0)
                {
                    job.CounterProviders.Add("System.Runtime");
                }

                foreach (var providerName in job.CounterProviders)
                {
                    if (MetadataProviders.TryGetValue(providerName, out var providerMetadata))
                    {
                        foreach (var metadata in providerMetadata)
                        {
                            job.Metadata.Enqueue(metadata);
                        }
                    }
                }

                Log.WriteLine($"Listening to counter event pipes (providers: {string.Join(", ", job.CounterProviders)})");

                try
                {
                    var providerList = job.CounterProviders
                        .Select(p => new Provider(
                            name: p,
                            eventLevel: EventLevel.Informational,
                            filterData: "EventCounterIntervalSec=1"))
                        .ToList();

                    var configuration = new SessionConfiguration(
                            circularBufferSizeMB: 1000,
                            format: EventPipeSerializationFormat.NetTrace,
                            providers: providerList);

                    EventPipeEventSource source = null;
                    Stream binaryReader = null;

                    var retries = 10;
                    while (retries-- > 0)
                    {
                        try
                        {
                            binaryReader = EventPipeClient.CollectTracing(job.ProcessId, configuration, out eventPipeSessionId);
                            break;
                        }
                        catch (TimeoutException)
                        {
                        }

                        await Task.Delay(100);
                    }

                    if (retries == 0)
                    {
                        Log.WriteLine("[ERROR] Failed to create counters event pipe client");
                        return;
                    }

                    source = new EventPipeEventSource(binaryReader);

                    source.Dynamic.All += (eventData) =>
                    {
                        // We only track event counters
                        if (!eventData.EventName.Equals("EventCounters"))
                        {
                            return;
                        }

                        var measurement = new Measurement();

                        var payloadVal = (IDictionary<string, object>)(eventData.PayloadValue(0));
                        var payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);

                        var counterName = payloadFields["Name"].ToString();

                        if (!job.Counters.TryGetValue(counterName, out var values))
                        {
                            lock (job.Counters)
                            {
                                if (!job.Counters.TryGetValue(counterName, out values))
                                {
                                    job.Counters[counterName] = values = new ConcurrentQueue<string>();
                                }
                            }
                        }

                        // Skip value if the provider is unknown
                        if (!MetadataProviderPrefixes.TryGetValue(eventData.ProviderName, out var prefix))
                        {
                            return;
                        }

                        measurement.Name = $"{prefix}/{counterName}";

                        switch (payloadFields["CounterType"])
                        {
                            case "Sum":
                                values.Enqueue(payloadFields["Increment"].ToString());
                                measurement.Value = payloadFields["Increment"];
                                break;
                            case "Mean":
                                values.Enqueue(payloadFields["Mean"].ToString());
                                measurement.Value = payloadFields["Mean"];
                                break;
                            default:
                                Log.WriteLine($"Unknown CounterType: {payloadFields["CounterType"]}");
                                break;
                        }

                        measurement.Timestamp = eventData.TimeStamp;

                        job.Measurements.Enqueue(measurement);
                    };

                    source.Process();
                }
                catch (Exception ex)
                {
                    if (ex.Message == "Read past end of stream.")
                    {
                        // Expected if the process has exited by itself
                        // and the event pipe is till trying to read from it
                    }
                    else
                    {
                        Log.WriteLine($"[ERROR] {ex.ToString()}");
                    }
                }
                finally
                {
                    eventPipeTerminated = true; // This indicates that the runtime is done. We shouldn't try to talk to it anymore.
                }
            });

            eventPipeTask.Start();
        }

        private static void StartMeasurement(ServerJob job)
        {
            measurementsTerminated = false;
            measurementsTask = new Task(async () =>
            {
                Log.WriteLine("Starting measurement");

                try
                {
                    var providerList = new List<Provider>()
                        {
                            new Provider(
                                name: "Benchmarks",
                                eventLevel: EventLevel.Verbose),
                        };

                    var configuration = new SessionConfiguration(
                            circularBufferSizeMB: 1000,
                            format: EventPipeSerializationFormat.NetTrace,
                            providers: providerList);

                    EventPipeEventSource source = null;
                    Stream binaryReader = null;

                    var retries = 10;
                    while (retries-- > 0)
                    {
                        try
                        {
                            binaryReader = EventPipeClient.CollectTracing(job.ProcessId, configuration, out measurementsSessionId);
                            break;
                        }
                        catch (TimeoutException)
                        {
                        }

                        await Task.Delay(100);
                    }

                    if (retries == 0)
                    {
                        Log.WriteLine("[ERROR] Failed to create measurements event pipe client");
                        return;
                    }

                    source = new EventPipeEventSource(binaryReader);

                    source.Dynamic.All += (eventData) =>
                    {
                        // We only track event counters for System.Runtime
                        if (eventData.ProviderName == "Benchmarks")
                        {
                            // TODO: Catch all event counters automatically
                            // And configure the filterData in the provider
                            //if (!eventData.EventName.Equals("EventCounters"))
                            //{
                            //job.Measurements.Enqueue(new Measurement
                            //{
                            //    Timestamp = eventData.TimeStamp,
                            //    Name = eventData.PayloadByName("name").ToString(),
                            //    Value = eventData.PayloadByName("value")
                            //});
                            //}

                            if (eventData.EventName.StartsWith("Measure"))
                            {
                                job.Measurements.Enqueue(new Measurement
                                {
                                    Timestamp = eventData.TimeStamp,
                                    Name = eventData.PayloadByName("name").ToString(),
                                    Value = eventData.PayloadByName("value")
                                });
                            }
                            else if (eventData.EventName == "Metadata")
                            {
                                job.Metadata.Enqueue(new MeasurementMetadata
                                {
                                    Source = "Benchmark",
                                    Name = eventData.PayloadByName("name").ToString(),
                                    Aggregate = Enum.Parse<Operation>(eventData.PayloadByName("aggregate").ToString(), true),
                                    Reduce = Enum.Parse<Operation>(eventData.PayloadByName("reduce").ToString(), true),
                                    ShortDescription = eventData.PayloadByName("shortDescription").ToString(),
                                    LongDescription = eventData.PayloadByName("longDescription").ToString(),
                                    Format = eventData.PayloadByName("format").ToString(),
                                });
                            }
                        }
                    };

                    source.Process();
                }
                catch (Exception ex)
                {
                    if (ex.Message == "Read past end of stream.")
                    {
                        // Expected if the process has exited by itself
                        // and the event pipe is till trying to read from it
                    }
                    else
                    {
                        Log.WriteLine($"[ERROR] {ex.ToString()}");
                    }
                }
                finally
                {
                    measurementsTerminated = true; // This indicates that the runtime is done. We shouldn't try to talk to it anymore.
                }
            });

            measurementsTask.Start();
        }

        private static void StartCollection(string workingDirectory, ServerJob job)
        {
            if (OperatingSystem == OperatingSystem.Windows)
            {
                job.PerfViewTraceFile = Path.Combine(job.BasePath, "benchmarks.etl.zip");
                var perfViewArguments = new Dictionary<string, string>();

                if (!String.IsNullOrEmpty(job.CollectArguments))
                {
                    foreach (var tuple in job.CollectArguments.Split(';'))
                    {
                        var values = tuple.Split(new char[] { '=' }, 2);
                        perfViewArguments[values[0]] = values.Length > 1 ? values[1] : "";
                    }
                }

                _startPerfviewArguments = $"";

                foreach (var customArg in perfViewArguments)
                {
                    var value = String.IsNullOrEmpty(customArg.Value) ? "" : $"={customArg.Value}";
                    _startPerfviewArguments += $" /{customArg.Key}{value}";
                }

                RunPerfview($"start /AcceptEula /NoGui {_startPerfviewArguments} \"{Path.Combine(job.BasePath, "benchmarks.trace")}\"", workingDirectory);
                Log.WriteLine($"Starting PerfView {_startPerfviewArguments}");
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
                perfCollectProcess = RunPerfcollect(perfviewArguments, workingDirectory);
            }
        }

        private static void StartDotNetTrace(int processId, ServerJob job)
        {
            job.PerfViewTraceFile = Path.Combine(job.BasePath, "trace.nettrace");

            dotnetTraceManualReset = new ManualResetEvent(false);
            dotnetTraceTask = Collect(dotnetTraceManualReset, processId, new FileInfo(job.PerfViewTraceFile), 256, job.DotNetTraceProviders, default(TimeSpan));

            if (dotnetTraceTask == null)
            {
                throw new Exception("NULL!!!");
            }
        }

        private static async Task UseMonoRuntimeAsync(string runtimeVersion, string outputFolder, string mode, Hardware? hardware)
        {
            if (String.IsNullOrEmpty(mode))
            {
                return;
            }

            string pkgNameSuffix = "";
            if (hardware == Hardware.ARM64)
            {
                pkgNameSuffix = "arm64";
            }
            else
            {
                pkgNameSuffix = "x64";
            }

            try
            {
                var packageName = "";

                switch (mode)
                {
                    case "jit":
                        packageName = $"Microsoft.NETCore.App.Runtime.Mono.linux-{pkgNameSuffix}".ToLowerInvariant();
                        break;
                    case "llvm-jit":
                    case "llvm-aot":
                        packageName = $"Microsoft.NETCore.App.Runtime.Mono.LLVM.AOT.linux-{pkgNameSuffix}".ToLowerInvariant();
                        break;
                    default:
                        throw new Exception("Invalid mono runtime moniker: " + mode);
                }

                var runtimePath = Path.Combine(_rootTempDir, "RuntimePackages", $"{packageName}.{runtimeVersion}.nupkg");

                // Ensure the folder already exists
                Directory.CreateDirectory(Path.GetDirectoryName(runtimePath));

                if (!File.Exists(runtimePath))
                {
                    Log.WriteLine($"Downloading mono runtime package");

                    var found = false;
                    foreach (var feed in _runtimeFeedUrls)
                    {
                        var url = $"{feed}/{packageName}/{runtimeVersion}/{packageName}.{runtimeVersion}.nupkg";

                        if (await DownloadFileAsync(url, runtimePath, maxRetries: 3, timeout: 60, throwOnError: false))
                        {
                            found = true;
                            break;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    if (!found)
                    {
                        throw new Exception("Mono runtime package not found");
                    }
                }
                else
                {
                    Log.WriteLine($"Found mono runtime package at '{runtimePath}'");
                }

                using (var archive = ZipFile.OpenRead(runtimePath))
                {
                    var systemCoreLib = archive.GetEntry($"runtimes/linux-{pkgNameSuffix}/native/System.Private.CoreLib.dll");
                    systemCoreLib.ExtractToFile(Path.Combine(outputFolder, "System.Private.CoreLib.dll"), true);

                    var libcoreclr = archive.GetEntry($"runtimes/linux-{pkgNameSuffix}/native/libcoreclr.so");
                    libcoreclr.ExtractToFile(Path.Combine(outputFolder, "libcoreclr.so"), true);
                }
            }
            catch (Exception e)
            {
                Log.WriteLine("ERROR: Failed to download mono runtime. " + e.ToString());
                throw;
            }
        }

        private static async Task AOT4Mono(string dotnetSdkVersion, string runtimeVersion, string outputFolder)
        {
            try
            {
                var fileName = "/bin/bash";

                //Download dotnet sdk package
                var dotnetMonoPath = Path.Combine(_rootTempDir, "dotnet-mono", $"dotnet-sdk-{dotnetSdkVersion}-linux-x64.tar.gz");
                var packageName = "Microsoft.NETCore.App.Runtime.Mono.LLVM.AOT.linux-x64".ToLowerInvariant();
                var runtimePath = Path.Combine(_rootTempDir, "RuntimePackages", $"{packageName}.{runtimeVersion}.nupkg");
                var llvmExtractDir = Path.Combine(Path.GetDirectoryName(runtimePath), "mono-llvm");

                if (!Directory.Exists(Path.GetDirectoryName(dotnetMonoPath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dotnetMonoPath));

                    Log.WriteLine($"Downloading dotnet skd package for mono AOT");

                    var found = false;
                    var url = $"https://dotnetcli.azureedge.net/dotnet/Sdk/{dotnetSdkVersion}/dotnet-sdk-{dotnetSdkVersion}-linux-x64.tar.gz";

                    if (await DownloadFileAsync(url, dotnetMonoPath, maxRetries: 3, timeout: 60, throwOnError: false))
                    {
                        found = true;
                    }

                    if (!found)
                    {
                        throw new Exception("dotnet sdk package not found");
                    }
                    else
                    {
                        var strCmdTar = $"tar -xf dotnet-sdk-{dotnetSdkVersion}-linux-x64.tar.gz";
                        var resultTar = ProcessUtil.Run(fileName, ConvertCmd2Arg(strCmdTar),
                                                        workingDirectory: Path.GetDirectoryName(dotnetMonoPath),
                                                        log: true);
                    }

                    Log.WriteLine($"Patching local dotnet with mono runtime and extracting llvm");

                    var strCmdGetVer = "./dotnet --list-runtimes | grep -i \"Microsoft.NETCore.App\"";
                    var resultGetVer = ProcessUtil.Run(fileName, ConvertCmd2Arg(strCmdGetVer),
                                                       workingDirectory: Path.GetDirectoryName(dotnetMonoPath),
                                                       log: true,
                                                       captureOutput: true);
                    var MicrosoftNETCoreAppPackageVersion = resultGetVer.StandardOutput.Split(' ')[1];

                    if (Directory.Exists(llvmExtractDir))
                    {
                        Directory.Delete(llvmExtractDir);
                    }
                    Directory.CreateDirectory(llvmExtractDir);

                    using (var archive = ZipFile.OpenRead(runtimePath))
                    {
                        var systemCoreLib = archive.GetEntry("runtimes/linux-x64/native/System.Private.CoreLib.dll");
                        systemCoreLib.ExtractToFile(Path.Combine(Path.GetDirectoryName(dotnetMonoPath), "shared", "Microsoft.NETCore.App", MicrosoftNETCoreAppPackageVersion, "System.Private.CoreLib.dll"), true);

                        var libcoreclr = archive.GetEntry("runtimes/linux-x64/native/libcoreclr.so");
                        libcoreclr.ExtractToFile(Path.Combine(Path.GetDirectoryName(dotnetMonoPath), "shared", "Microsoft.NETCore.App", MicrosoftNETCoreAppPackageVersion, "libcoreclr.so"), true);

                        var llcExe = archive.GetEntry("runtimes/linux-x64/native/llc");
                        llcExe.ExtractToFile(Path.Combine(llvmExtractDir, "llc"), true);

                        var optExe = archive.GetEntry("runtimes/linux-x64/native/opt");
                        optExe.ExtractToFile(Path.Combine(llvmExtractDir, "opt"), true);
                    }
                    var strCmdChmod = "chmod +x opt llc";
                    var resultChmod = ProcessUtil.Run(fileName, ConvertCmd2Arg(strCmdChmod),
                                                      workingDirectory: llvmExtractDir,
                                                      log: true);

                }
                else
                {
                    Log.WriteLine($"Found local dotnet with mono runtime at '{Path.GetDirectoryName(dotnetMonoPath)}'");
                }

                Log.WriteLine("Pre-compile assemblies inside publish folder");

                var strCmdPreCompile = $@"for assembly in {outputFolder}/*.dll; do
                                              PATH={llvmExtractDir}:${{PATH}} MONO_ENV_OPTIONS=--aot=llvm,mcpu=native ./dotnet $assembly;
                                           done";
                var resultPreCompile = ProcessUtil.Run(fileName, ConvertCmd2Arg(strCmdPreCompile),
                                                   workingDirectory: Path.GetDirectoryName(dotnetMonoPath),
                                                   log: true,
                                                   captureOutput: true);
            }
            catch (Exception e)
            {
                Log.WriteLine("ERROR: Failed to AOT for mono. " + e.ToString());
                throw;
            }
        }

        private static string ConvertCmd2Arg(string cmd)
        {
            cmd.Replace("\"", "\"\"");
            var result = $"-c \"{cmd}\"";
            return result;
        }

        private static bool MarkAsRunning(string hostname, ServerJob job, Stopwatch stopwatch)
        {
            lock (job)
            {
                // Already executed this method?
                if (job.State == ServerState.Running)
                {
                    return false;
                }

                job.StartupMainMethod = stopwatch.Elapsed;

                job.Measurements.Enqueue(new Measurement
                {
                    Name = "benchmarks/start-time",
                    Timestamp = DateTime.UtcNow,
                    Value = stopwatch.ElapsedMilliseconds
                });

                Log.WriteLine($"Running job '{job.Id}' with scenario '{job.Scenario}'");
                job.Url = ComputeServerUrl(hostname, job);

                // Mark the job as running to allow the Client to start the test
                job.State = ServerState.Running;

                return true;
            }
        }

        private static string GenerateApplicationHostConfig(ServerJob job, string publishedFolder, string executable, string arguments,
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

                var ancmPath = Path.Combine(publishedFolder, "x64\\aspnetcorev2.dll");
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

                // The SDK generates a web.config file on publish, which will conflict with apphost.config
                try
                {
                    File.Delete(Path.Combine(publishedFolder, "web.config"));
                }
                catch (Exception)
                {
                }

                // But if a specific web.benchmnarks.config is provided, use it
                try
                {
                    File.Move(Path.Combine(publishedFolder, "web.config"), Path.Combine(publishedFolder, "web.benchmarks.config"));
                }
                catch (Exception)
                {
                }

                return fileName;
            }
        }

        private static double GetSwapBytes()
        {
            var result = ProcessUtil.Run("cat", "/proc/meminfo", throwOnError: false, captureOutput: true);

            // SwapTotal:       8388604 kB
            // SwapFree:        8310012 kB

            int swapTotal = 0, swapFree = 0;
            bool totalFound = false, freeFound = false;

            using (var sr = new StringReader(result.StandardOutput))
            {
                var line = sr.ReadLine();

                while (line != null && !(totalFound && freeFound))
                {
                    if (line.StartsWith("SwapTotal"))
                    {
                        totalFound = true;
                        swapTotal = ParseMeminfo(line);
                    }

                    if (line.StartsWith("SwapFree"))
                    {
                        freeFound = true;
                        swapFree = ParseMeminfo(line);
                    }

                    line = sr.ReadLine();
                }
            }

            if (!totalFound || !freeFound)
            {
                return -1;
            }

            var swapkB = swapTotal - swapFree;

            return swapkB * 1024;

            int ParseMeminfo(string line)
            {
                return int.Parse(line.Split(':', StringSplitOptions.RemoveEmptyEntries)[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
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

        private static async Task<string> GetFlatContainerVersion(string packageIndexUrl, string versionPrefix)
        {
            Log.WriteLine($"Downloading flatcontainer ...");
            var root = JObject.Parse(await DownloadContentAsync(packageIndexUrl));

            var matchingVersions = root["versions"]
                .Select(x => x.ToString())
                // Unlisting these versions manually as they are breaking the order of 5.0.0-alpha.X
                .Where(x => !x.StartsWith("5.0.0-alpha1"))
                .Where(t => t.StartsWith(versionPrefix))
                .Select(x => new NuGetVersion(x))
                .ToArray()
                ;

            // Extract the highest version
            var latest = matchingVersions
                .OrderByDescending(v => v, VersionComparer.Default)
                .FirstOrDefault();

            return latest?.OriginalVersion;
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

        private static string FormatPathSeparators(string path)
        {
            if (String.IsNullOrEmpty(path))
            {
                return "";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return path.Replace("/", "\\");
            }
            else
            {
                return path.Replace("\\", "/");
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

        /// <param name="providers">
        /// A profile name, or a list of comma separated EventPipe providers to be enabled.
        /// c.f. https://github.com/dotnet/diagnostics/blob/master/documentation/dotnet-trace-instructions.md
        /// </param>
        private static async Task<int> Collect(ManualResetEvent shouldExit, int processId, FileInfo output, uint buffersize, string providers, TimeSpan duration)
        {
            if (String.IsNullOrWhiteSpace(providers))
            {
                providers = "cpu-sampling";
            }

            if (!TraceExtensions.DotNETRuntimeProfiles.TryGetValue(providers, out var providerCollection))
            {
                providerCollection = TraceExtensions.ToProviders(providers).ToArray();
            }

            if (providerCollection.Length <= 0)
            {
                Log.WriteLine($"Tracing arguments not valid: {providers}");

                return -1;
            }

            var process = Process.GetProcessById(processId);
            var configuration = new SessionConfiguration(
                circularBufferSizeMB: buffersize,
                format: EventPipeSerializationFormat.NetTrace,
                providers: providerCollection);

            var shouldStopAfterDuration = duration != default(TimeSpan);
            var failed = false;
            var terminated = false;
            System.Timers.Timer durationTimer = null;

            Log.WriteLine($"Tracing process {processId} on file {output.FullName}");

            ulong sessionId = 0;
            using (Stream stream = EventPipeClient.CollectTracing(processId, configuration, out sessionId))
            {
                if (sessionId == 0)
                {
                    return -1;
                }

                if (shouldStopAfterDuration)
                {
                    durationTimer = new System.Timers.Timer(duration.TotalMilliseconds);
                    durationTimer.Elapsed += (s, e) => shouldExit.Set();
                    durationTimer.AutoReset = false;
                }

                var collectingTask = new Task(() =>
                {
                    try
                    {
                        var stopwatch = new Stopwatch();
                        durationTimer?.Start();
                        stopwatch.Start();

                        using (var fs = new FileStream(output.FullName, FileMode.Create, FileAccess.Write))
                        {
                            var buffer = new byte[16 * 1024];

                            while (true)
                            {
                                int nBytesRead = stream.Read(buffer, 0, buffer.Length);
                                if (nBytesRead <= 0)
                                    break;
                                fs.Write(buffer, 0, nBytesRead);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"Tracing failed with exception {ex}");

                        failed = true;
                    }
                    finally
                    {
                        terminated = true;
                        shouldExit.Set();

                        Log.WriteLine($"Tracing terminated.");
                    }
                });
                collectingTask.Start();

                do
                {
                    await Task.Delay(100);
                } while (!shouldExit.WaitOne(0));

                Log.WriteLine($"Tracing stopped");

                if (!terminated)
                {
                    durationTimer?.Stop();
                    EventPipeClient.StopTracing(processId, sessionId);
                }

                await collectingTask;
            }

            durationTimer?.Dispose();

            return failed ? -1 : 0;
        }

        public static Task EnsureDotnetInstallExistsAsync()
        {
            Log.WriteLine($"Checking requirements...");

            if (String.IsNullOrEmpty(_rootTempDir))
            {
                // From the /tmp folder (in Docker, should be mounted to /mnt/benchmarks) use a specific 'benchmarksserver' root folder to isolate from other services
                // that use the temp folder, and create a sub-folder (process-id) for each server running.
                // The cron job is responsible for cleaning the folders
                _rootTempDir = Path.Combine(Path.GetTempPath(), "benchmarks-agent", $"benchmarks-server-{Process.GetCurrentProcess().Id}");

                if (Directory.Exists(_rootTempDir))
                {
                    Directory.Delete(_rootTempDir, true);
                }
            }

            Directory.CreateDirectory(_rootTempDir);

            if (String.IsNullOrEmpty(_dotnethome))
            {
                _dotnethome = GetTempDir();
            }

            // Add a Nuget.config for the self-contained deployments to be able to find the runtime packages on the CI feeds

            var rootNugetConfig = Path.Combine(_rootTempDir, "NuGet.Config");

            if (!File.Exists(rootNugetConfig))
            {
                File.WriteAllText(rootNugetConfig, @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <add key=""benchmarks-dotnet5"" value=""https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5/nuget/v3/index.json"" />
    <add key=""benchmarks-dotnet5-transport"" value=""https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet5-transport/nuget/v3/index.json"" />
    <add key=""benchmarks-aspnetcore"" value=""https://dotnetfeed.blob.core.windows.net/aspnet-aspnetcore/index.json"" />
    <add key=""benchmarks-dotnet-core"" value=""https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json"" />
    <add key=""benchmarks-extensions"" value=""https://dotnetfeed.blob.core.windows.net/aspnet-extensions/index.json"" />
    <add key=""benchmarks-aspnetcore-tooling"" value=""https://dotnetfeed.blob.core.windows.net/aspnet-aspnetcore-tooling/index.json"" />
    <add key=""benchmarks-entityframeworkcore"" value=""https://dotnetfeed.blob.core.windows.net/aspnet-entityframeworkcore/index.json"" />
    <add key=""benchmarks-nuget"" value=""https://api.nuget.org/v3/index.json"" />
  </packageSources>
</configuration>
");
            }

            // Download PerfView
            if (OperatingSystem == OperatingSystem.Windows)
            {
                if (String.IsNullOrEmpty(_perfviewPath))
                {
                    _perfviewPath = Path.Combine(Path.GetTempPath(), PerfViewVersion, Path.GetFileName(_perfviewUrl));
                }

                // Ensure the folder already exists
                Directory.CreateDirectory(Path.GetDirectoryName(_perfviewPath));

                if (!File.Exists(_perfviewPath))
                {
                    Log.WriteLine($"Downloading PerfView to '{_perfviewPath}'");
                    DownloadFileAsync(_perfviewUrl, _perfviewPath, maxRetries: 5, timeout: 60).GetAwaiter().GetResult();
                }
            }

            if (String.IsNullOrEmpty(_dotnetInstallPath))
            {
                // Download dotnet-install at startup, once.
                _dotnetInstallPath = Path.Combine(_rootTempDir, Path.GetRandomFileName());
            }

            // Ensure the folder already exists
            Directory.CreateDirectory(_dotnetInstallPath);

            var _dotnetInstallUrl = OperatingSystem == OperatingSystem.Windows
                ? _dotnetInstallPs1Url
                : _dotnetInstallShUrl
                ;

            var dotnetInstallFilename = Path.Combine(_dotnetInstallPath, Path.GetFileName(_dotnetInstallUrl));

            if (!File.Exists(dotnetInstallFilename))
            {
                Log.WriteLine($"Downloading dotnet-install to '{dotnetInstallFilename}'");
                return DownloadFileAsync(_dotnetInstallUrl, dotnetInstallFilename, maxRetries: 5, timeout: 60);
            }

            return Task.CompletedTask;
        }

        private void OnShutdown()
        {
            try
            {
                Log.WriteLine("Cleaning up temporary folder...");

                // build servers will hold locks on dotnet.exe otherwise
                // c.f. https://github.com/dotnet/sdk/issues/9487

                // If dotnet hasn't yet been installed, don't try to shutdown the build servers
                if (File.Exists(GetDotNetExecutable(_dotnethome)))
                {
                    ProcessUtil.Run(GetDotNetExecutable(_dotnethome), "build-server shutdown", workingDirectory: _dotnethome, log: true);
                }

                if (_cleanup && Directory.Exists(_rootTempDir))
                {
                    TryDeleteDir(_rootTempDir, false);
                }
            }
            finally
            {
            }
        }
    }
}
