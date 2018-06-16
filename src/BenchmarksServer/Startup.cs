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
        private static string CurrentAspNetCoreVersion = "2.1.0";
        private static string CurrentTargetFramework = "netcoreapp2.1";

        private const string PerfViewVersion = "P2.0.12";

        private static readonly HttpClient _httpClient;
        private static readonly HttpClientHandler _httpClientHandler;
        private static readonly string _dotnetInstallShUrl = "https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.sh";
        private static readonly string _dotnetInstallPs1Url = "https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain/dotnet-install.ps1";
        private static readonly string _latestAspnetCoreRuntimeUrl = "https://dotnet.myget.org/F/aspnetcore-dev/api/v3/registration1/Microsoft.AspNetCore.App/index.json";
        private static readonly string _currentDotnetRuntimeUrl = "https://dotnetcli.blob.core.windows.net/dotnet/Runtime/Current/latest.version";
        private static readonly string _edgeDotnetRuntimeUrl = "https://dotnetcli.blob.core.windows.net/dotnet/Runtime/master/latest.version";
        private static readonly string _sdkVersionUrl = "https://raw.githubusercontent.com/aspnet/BuildTools/dev/files/KoreBuild/config/sdk.version";
        private static readonly string _universeDependenciesUrl = "https://raw.githubusercontent.com/aspnet/Universe/dev/build/dependencies.props";
        private static readonly string _perfviewUrl = $"https://github.com/Microsoft/perfview/releases/download/{PerfViewVersion}/PerfView.exe";

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
                    ServerJob job = null;

                    // Find the first job that is not in Initializing state
                    foreach(var j in _jobs.GetAll())
                    {
                        if (j.State == ServerState.Initializing)
                        {
                            var now = DateTime.UtcNow;

                            if (now - j.LastDriverCommunicationUtc > TimeSpan.FromSeconds(30))
                            {
                                // The job needs to be deleted
                                Log.WriteLine($"Driver didn't communicate for {now - j.LastDriverCommunicationUtc}. Halting job.");
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
                        string dotnetDir = dotnetHome;
                        string benchmarksDir = null;
                        var standardOutput = new StringBuilder();
                        var startMonitorTime = DateTime.UtcNow;

                        if (job.State == ServerState.Failed)
                        {
                            var now = DateTime.UtcNow;

                            // Clean the job in case the driver is not running
                            if (now - job.LastDriverCommunicationUtc > TimeSpan.FromSeconds(30))
                            {
                                Log.WriteLine($"Driver didn't communicate for {now - job.LastDriverCommunicationUtc}. Halting job.");
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

                                    if (benchmarksDir != null && dotnetDir != null)
                                    {
                                        Debug.Assert(process == null);
                                        process = await StartProcess(hostname, Path.Combine(tempDir, benchmarksDir), job, dotnetDir, standardOutput);

                                        job.ProcessId = process.Id;
                                    }
                                    else
                                    {
                                        Log.WriteLine($"Job failed with CloneRestoreAndBuild");
                                        job.State = ServerState.Failed;
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
                                        if (now - job.LastDriverCommunicationUtc > TimeSpan.FromSeconds(30))
                                        {
                                            Log.WriteLine($"Driver didn't communicate for {now - job.LastDriverCommunicationUtc}. Halting job.");
                                            job.State = ServerState.Deleting;
                                        }

                                        if (process != null)
                                        {
                                            if (process.HasExited && process.ExitCode != 0)
                                            {
                                                Log.WriteLine($"Job failed");

                                                job.Error = "Job failed at runtime\n" + standardOutput.ToString();
                                                job.State = ServerState.Failed;
                                            }
                                            else
                                            {

                                                // TODO: Accessing the TotalProcessorTime on OSX throws so just leave it as 0 for now
                                                // We need to dig into this
                                                var newCPUTime = OperatingSystem == OperatingSystem.OSX ? TimeSpan.Zero : process.TotalProcessorTime;
                                                var elapsed = now.Subtract(lastMonitorTime).TotalMilliseconds;
                                                var cpu = Math.Round((newCPUTime - oldCPUTime).TotalMilliseconds / (Environment.ProcessorCount * elapsed) * 100);
                                                lastMonitorTime = now;
                                                oldCPUTime = newCPUTime;

                                                process.Refresh();

                                                job.AddServerCounter(new ServerCounter
                                                {
                                                    Elapsed = now - startMonitorTime,
                                                    WorkingSet = process.WorkingSet64,
                                                    CpuPercentage = cpu
                                                });
                                            }
                                        }
                                        else if (!String.IsNullOrEmpty(dockerImage))
                                        {
                                            var output = new StringBuilder();

                                            // Get docker stats
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
                            if (DateTime.UtcNow - startMonitorTime > TimeSpan.FromSeconds(30))
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

                                process = null;
                            }
                            else if (!String.IsNullOrEmpty(dockerImage))
                            {
                                DockerCleanUp(dockerContainerId, dockerImage);
                                dockerImage = null;
                            }

                            job.State = ServerState.Stopped;
                            Log.WriteLine($"Process stopped");
                        }

                        async Task DeleteJobAsync()
                        {
                            await StopJobAsync();

                            if (_cleanup && !job.NoClean && tempDir != null)
                            {
                                DeleteDir(tempDir);
                            }

                            // If a custom dotnet directory was used, clean it
                            if (_cleanup && !job.NoClean && dotnetDir != dotnetHome)
                            {
                                DeleteDir(dotnetDir);
                            }

                            // Clean attachments
                            foreach (var attachment in job.Attachments)
                            {
                                try
                                {
                                    File.Delete(attachment.TempFilename);
                                }
                                catch
                                {
                                    Log.WriteLine($"Error while deleting attachment '{attachment.TempFilename}'");
                                }
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

        private static async Task<(string containerId, string imageName)> DockerBuildAndRun(string path, ServerJob job, string hostname)
        {
            var source = job.Source;
            // Docker image names must be lowercase
            var imageName = $"benchmarks_{source.DockerImageName}".ToLowerInvariant();
            var cloneDir = Path.Combine(path, Git.Clone(path, source.Repository));
            var workingDirectory = Path.Combine(cloneDir, source.DockerContextDirectory);

            if (!string.IsNullOrEmpty(source.BranchOrCommit))
            {
                Git.Checkout(cloneDir, source.BranchOrCommit);
            }

            ProcessUtil.Run("docker", $"build --pull --no-cache -t {imageName} -f {source.DockerFile} {workingDirectory}", workingDirectory: cloneDir);

            // Only run on the host network on linux
            var useHostNetworking = OperatingSystem == OperatingSystem.Linux;

            var environmentArguments = "";

            foreach (var env in job.EnvironmentVariables)
            {
                environmentArguments += $"--env {env.Key}={env.Value} ";
            }

            var command = useHostNetworking ? $"run -d {environmentArguments} {job.Arguments} --network host {imageName}" :
                                              $"run -d {environmentArguments} {job.Arguments} -p {job.Port}:{job.Port} {imageName}";

            var result = ProcessUtil.Run("docker", $"{command} ");
            var containerId = result.StandardOutput.Trim();
            job.Url = ComputeServerUrl(hostname, job);

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

                        if (job.State == ServerState.Starting && e.Data.IndexOf(job.ReadyStateText, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Log.WriteLine($"Application is now running...");
                            job.State = ServerState.Running;
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

                job.State = ServerState.Running;
            }

            return (containerId, imageName);
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

        private static void DockerCleanUp(string containerId, string imageName)
        {
            var result = ProcessUtil.Run("docker", $"logs {containerId}");
            Console.WriteLine(result.StandardOutput);

            ProcessUtil.Run("docker", $"stop {containerId}");

            ProcessUtil.Run("docker", $"rmi --force {imageName}");
        }

        private static async Task<(string benchmarkDir, string dotnetDir)> CloneRestoreAndBuild(string path, ServerJob job, string dotnetHome)
        {
            // It's possible that the user specified a custom branch/commit for the benchmarks repo,
            // so we need to add that to the set of sources to restore if it's not already there.
            //
            // Note that this is also going to de-dupe the repos if the same one was specified twice at
            // the command-line (last first to support overrides).
            var repos = new HashSet<Source>(SourceRepoComparer.Instance);

            repos.Add(job.Source);

            // Clone
            string benchmarkedDir = null;

            if (job.Source.SourceCode != null)
            {
                benchmarkedDir = Path.Combine(path, "src");

                ZipFile.ExtractToDirectory(job.Source.SourceCode.TempFilename, benchmarkedDir);
            }
            else
            {
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

            Log.WriteLine("Installing dotnet runtimes and sdk");

            // Computes the location of the benchmarked app
            var benchmarkedApp = Path.Combine(path, benchmarkedDir, Path.GetDirectoryName(job.Source.Project));

            var sdkVersion = (await ReadUrlStringAsync(_sdkVersionUrl, maxRetries: 5)).Trim();
            Log.WriteLine($"Detecting compatible SDK version: {sdkVersion}");

            // In theory the actual latest runtime version should be taken from the dependencies.pros file from
            // https://dotnet.myget.org/feed/aspnetcore-dev/package/nuget/Internal.AspNetCore.Universe.Lineup
            // however this is different only if the coherence build didn't go through.

            // Define which Runtime and SDK will be installed.

            string targetFramework;
            string runtimeFrameworkVersion;
            string aspNetCoreVersion;
            string actualAspNetCoreVersion;

            if (!String.Equals(job.RuntimeVersion, "Current", StringComparison.OrdinalIgnoreCase))
            {
                // Default targetFramework
                targetFramework = "netcoreapp2.2";

                if (String.Equals(job.RuntimeVersion, "Latest", StringComparison.OrdinalIgnoreCase))
                {
                    runtimeFrameworkVersion = await GetLatestRuntimeVersion(buildToolsPath);
                }
                else if (String.Equals(job.RuntimeVersion, "Edge", StringComparison.OrdinalIgnoreCase))
                {
                    runtimeFrameworkVersion = await GetEdgeRuntimeVersion(buildToolsPath);
                }
                else
                {
                    // Custom version
                    runtimeFrameworkVersion = job.RuntimeVersion;

                    if (runtimeFrameworkVersion.StartsWith("2.0"))
                    {
                        targetFramework = "netcoreapp2.0";
                    }
                    else if (runtimeFrameworkVersion.StartsWith("2.1"))
                    {
                        targetFramework = "netcoreapp2.1";
                    }
                    else
                    {
                        targetFramework = "netcoreapp2.2";
                    }
                }
            }
            else
            {
                runtimeFrameworkVersion = await GetCurrentRuntimeVersion(buildToolsPath);
                targetFramework = CurrentTargetFramework;
            }

            var globalJson = "{ \"sdk\": { \"version\": \"" + sdkVersion + "\" } }";
            Log.WriteLine($"Writing global.json with content: {globalJson}");
            File.WriteAllText(Path.Combine(benchmarkedApp, "global.json"), globalJson);

            // Define which ASP.NET Core packages version to use
            switch (job.AspNetCoreVersion.ToLowerInvariant())
            {
                case "current":
                    aspNetCoreVersion = "2.1.*";
                    actualAspNetCoreVersion = CurrentAspNetCoreVersion;
                    break;
                case "latest":
                    aspNetCoreVersion = "2.2-*";
                    actualAspNetCoreVersion = await GetLatestAspNetCoreRuntimeVersion(buildToolsPath);
                    break;
                default:
                    aspNetCoreVersion = job.AspNetCoreVersion;
                    actualAspNetCoreVersion = aspNetCoreVersion;
                    break;
            }

            if (OperatingSystem == OperatingSystem.Windows)
            {
                if (!_installedRuntimes.Contains("Current"))
                {
                    // Install latest stable 2.0 SDK version (and associated runtime)
                    ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted .\\dotnet-install.ps1 -Channel Current -NoPath -SkipNonVersionedFiles",
                        workingDirectory: _dotnetInstallPath,
                        environmentVariables: env));

                    _installedRuntimes.Add("Current");
                }

                if (!_installedSdks.Contains(sdkVersion))
                {
                    // Install latest SDK version (and associated runtime)
                    ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted .\\dotnet-install.ps1 -Version {sdkVersion} -NoPath -SkipNonVersionedFiles",
                    workingDirectory: _dotnetInstallPath,
                    environmentVariables: env));

                    _installedSdks.Add(sdkVersion);
                }

                if (!_installedRuntimes.Contains(runtimeFrameworkVersion))
                {
                    // Install runtime required for this scenario
                    ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted .\\dotnet-install.ps1 -Version {runtimeFrameworkVersion} -Runtime dotnet -NoPath -SkipNonVersionedFiles",
                    workingDirectory: _dotnetInstallPath,
                    environmentVariables: env));

                    _installedRuntimes.Add(runtimeFrameworkVersion);
                }

                // The aspnet core runtime is only available for >= 2.1, in 2.0 the dlls are contained in the runtime store
                if (job.UseRuntimeStore && targetFramework != "netcoreapp2.0" && !_installedAspNetRuntimes.Contains(actualAspNetCoreVersion))
                {
                    // Install aspnet runtime required for this scenario
                    ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("powershell", $"-NoProfile -ExecutionPolicy unrestricted .\\dotnet-install.ps1 -Version {actualAspNetCoreVersion} -Runtime aspnetcore -NoPath -SkipNonVersionedFiles",
                    workingDirectory: _dotnetInstallPath,
                    environmentVariables: env));

                    _installedAspNetRuntimes.Add(actualAspNetCoreVersion);
                }
            }
            else
            {
                if (!_installedRuntimes.Contains("Current"))
                {
                    // Install latest stable SDK version (and associated runtime)
                    ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --channel Current --no-path --skip-non-versioned-files",
                    workingDirectory: _dotnetInstallPath,
                    environmentVariables: env));
                    _installedRuntimes.Add("Current");
                }

                if (!_installedSdks.Contains(sdkVersion))
                {
                    // Install latest SDK version (and associated runtime)
                    ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --version {sdkVersion} --no-path --skip-non-versioned-files",
                    workingDirectory: _dotnetInstallPath,
                    environmentVariables: env));
                    _installedSdks.Add(sdkVersion);
                }

                if (!_installedRuntimes.Contains(runtimeFrameworkVersion))
                {
                    // Install runtime required by coherence universe
                    ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --version {runtimeFrameworkVersion} --runtime dotnet --no-path --skip-non-versioned-files",
                    workingDirectory: _dotnetInstallPath,
                    environmentVariables: env));

                    _installedRuntimes.Add(runtimeFrameworkVersion);
                }

                // The aspnet core runtime is only available for >= 2.1, in 2.0 the dlls are contained in the runtime store
                if (job.UseRuntimeStore && targetFramework != "netcoreapp2.0" && !_installedAspNetRuntimes.Contains(actualAspNetCoreVersion))
                {
                    // Install runtime required by coherence universe
                    ProcessUtil.RetryOnException(3, () => ProcessUtil.Run("/usr/bin/env", $"bash dotnet-install.sh --version {actualAspNetCoreVersion} --runtime aspnetcore --no-path --skip-non-versioned-files",
                    workingDirectory: _dotnetInstallPath,
                    environmentVariables: env));

                    _installedAspNetRuntimes.Add(actualAspNetCoreVersion);
                }
            }

            var dotnetDir = dotnetHome;

            // Updating ServerJob to reflect actual versions used
            job.AspNetCoreVersion = actualAspNetCoreVersion;
            job.RuntimeVersion = runtimeFrameworkVersion;
            job.SdkVersion = sdkVersion;

            // Build and Restore
            var dotnetExecutable = GetDotNetExecutable(dotnetDir);

            var buildParameters = $"/p:BenchmarksAspNetCoreVersion={actualAspNetCoreVersion} " +
                $"/p:MicrosoftAspNetCoreAllPackageVersion={actualAspNetCoreVersion} " +
                $"/p:MicrosoftAspNetCoreAppPackageVersion={actualAspNetCoreVersion} " +
                $"/p:BenchmarksNETStandardImplicitPackageVersion={aspNetCoreVersion} " +
                $"/p:BenchmarksNETCoreAppImplicitPackageVersion={aspNetCoreVersion} " +
                $"/p:BenchmarksRuntimeFrameworkVersion={runtimeFrameworkVersion} " +
                $"/p:BenchmarksTargetFramework={targetFramework} ";

            if (targetFramework == "netcoreapp2.0")
            {
                buildParameters += $"/p:MicrosoftNETCoreApp20PackageVersion={runtimeFrameworkVersion} ";
                if (!job.UseRuntimeStore)
                {
                    buildParameters += $"/p:PublishWithAspNetCoreTargetManifest=false ";
                }
            }
            else if (targetFramework == "netcoreapp2.1")
            {
                buildParameters += $"/p:MicrosoftNETCoreApp21PackageVersion={runtimeFrameworkVersion} ";
                if (!job.UseRuntimeStore)
                {
                    buildParameters += $"/p:MicrosoftNETPlatformLibrary=Microsoft.NETCore.App ";
                }
            }
            else if (targetFramework == "netcoreapp2.2")
            {
                buildParameters += $"/p:MicrosoftNETCoreApp22PackageVersion={runtimeFrameworkVersion} ";
                if (!job.UseRuntimeStore)
                {
                    buildParameters += $"/p:MicrosoftNETPlatformLibrary=Microsoft.NETCore.App ";
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported framework: {targetFramework}");
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

                return (null, null);
            }

            Log.WriteLine($"Application published successfully in {DateTime.UtcNow - startPublish}");

            // Copy crossgen in the app folder
            if (job.Collect && OperatingSystem == OperatingSystem.Linux)
            {
                Log.WriteLine("Copying crossgen to application folder");

                // Downloading corresponding package

                var runtimePath = Path.Combine(_rootTempDir, "RuntimePackages", $"runtime.linux-x64.Microsoft.NETCore.App.{runtimeFrameworkVersion}.nupkg");

                // Ensure the folder already exists
                Directory.CreateDirectory(Path.GetDirectoryName(runtimePath));

                if (!File.Exists(runtimePath))
                {
                    Log.WriteLine($"Downloading runtime package");
                    await DownloadFileAsync($"https://dotnet.myget.org/F/dotnet-core/api/v2/package/runtime.linux-x64.Microsoft.NETCore.App/{runtimeFrameworkVersion}", runtimePath, maxRetries: 5, timeout: 60);
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
                                ? Path.Combine(outputFolder, "crossgen")
                                : Path.Combine(dotnetDir, "shared", "Microsoft.NETCore.App", runtimeFrameworkVersion)
                                ;

                            var crossgenFilename = Path.Combine(crossgenFolder, "crossgen");

                            if (!File.Exists(crossgenFilename))
                            {
                                entry.ExtractToFile(crossgenFilename);
                                Log.WriteLine($"Copied crossgen to {crossgenFolder}");
                            }

                            break;
                        }
                    }
                }
            }

            // Copy all output attachments
            foreach (var attachment in job.Attachments.Where(x => x.Location == AttachmentLocation.Output))
            {
                var filename = Path.Combine(outputFolder, attachment.Filename.Replace("\\", "/"));

                Log.WriteLine($"Creating output file: {filename}");

                if (File.Exists(filename))
                {
                    File.Delete(filename);
                }

                File.Copy(attachment.TempFilename, filename);
            }

            // Copy all runtime attachments in all runtime folders
            foreach (var attachment in job.Attachments.Where(x => x.Location == AttachmentLocation.Runtime))
            {
                var runtimeFolder = Path.Combine(dotnetDir, "shared", "Microsoft.NETCore.App", runtimeFrameworkVersion);

                var filename = Path.Combine(runtimeFolder, attachment.Filename.Replace("\\", "/"));

                Log.WriteLine($"Creating runtime file: {filename}");

                if (File.Exists(filename))
                {
                    File.Delete(filename);
                }

                File.Copy(attachment.TempFilename, filename);
            }

            return (benchmarkedDir, dotnetDir);
        }

        /// <summary>
        /// Retrieves the runtime version used on ASP.NET Coherence builds
        /// </summary>
        private static async Task<string> GetLatestRuntimeVersion(string buildToolsPath)
        {
            var universeDependenciesPath = Path.Combine(buildToolsPath, Path.GetFileName(_universeDependenciesUrl));
            await DownloadFileAsync(_universeDependenciesUrl, universeDependenciesPath, maxRetries: 5, timeout: 10);
            var latestRuntimeVersion = XDocument.Load(universeDependenciesPath).Root
                .Element("PropertyGroup")
                .Element("MicrosoftNETCoreAppPackageVersion")
                .Value;

            Log.WriteLine($"Detecting Universe Coherence runtime version: {latestRuntimeVersion}");
            return latestRuntimeVersion;
        }

        /// <summary>
        /// Retrieves the latest coherent ASP.NET version
        /// </summary>
        private static async Task<string> GetLatestAspNetCoreRuntimeVersion(string buildToolsPath)
        {
            var aspnetCoreRuntimePath = Path.Combine(buildToolsPath, "aspnetCoreRuntimePath.json");
            await DownloadFileAsync(_latestAspnetCoreRuntimeUrl, aspnetCoreRuntimePath, maxRetries: 5, timeout: 10);
            var aspnetCoreRuntime = JObject.Parse(File.ReadAllText(aspnetCoreRuntimePath));

            var latestAspNetCoreRuntime = (string)aspnetCoreRuntime["items"].Last()["upper"];


            Log.WriteLine($"Detecting ASP.NET runtime version: {latestAspNetCoreRuntime}");
            return latestAspNetCoreRuntime;
        }

        /// <summary>
        /// Retrieves the latest runtime version
        /// </summary>
        /// <param name="buildToolsPath"></param>
        /// <returns></returns>
        private static async Task<string> GetEdgeRuntimeVersion(string buildToolsPath)
        {
            var edgeRuntimePath = Path.Combine(buildToolsPath, "edgeDotnetRuntimeVersion.txt");
            await DownloadFileAsync(_edgeDotnetRuntimeUrl, edgeRuntimePath, maxRetries: 5, timeout: 10);
            var content = await File.ReadAllLinesAsync(edgeRuntimePath);

            // Read the last line that contains the version
            var edgeDotnetRuntime = content.Last();

            Log.WriteLine($"Detecting edge runtime version: {edgeDotnetRuntime}");
            return edgeDotnetRuntime;
        }

        /// <summary>
        /// Retrieves the Current runtime version
        /// </summary>
        private static async Task<string> GetCurrentRuntimeVersion(string buildToolsPath)
        {
            var currentRuntimePath = Path.Combine(buildToolsPath, "currentDotnetRuntimeVersion.txt");
            await DownloadFileAsync(_currentDotnetRuntimeUrl, currentRuntimePath, maxRetries: 5, timeout: 10);
            var content = await File.ReadAllLinesAsync(currentRuntimePath);

            // Read the last line that contains the version
            var currentDotnetRuntime = content.Last();

            Log.WriteLine($"Detecting current runtime version: {currentDotnetRuntime}");
            return currentDotnetRuntime;
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

        private static async Task<string> ReadUrlStringAsync(string url, int maxRetries, int timeout = 5)
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
                    using (var stream = new MemoryStream())
                    {
                        // Copy the response stream directly to the file stream
                        await response.Content.CopyToAsync(stream);
                        return Encoding.UTF8.GetString(stream.ToArray());
                    }                    
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
            var serverUrl = $"{job.Scheme.ToString().ToLowerInvariant()}://{hostname}:{job.Port}";
            var executable = GetDotNetExecutable(dotnetHome);
            var projectFilename = Path.GetFileNameWithoutExtension(job.Source.Project);
            var benchmarksDll = Path.Combine("published", $"{projectFilename}.dll");
            var iis = job.WebHost == WebHost.IISInProcess || job.WebHost == WebHost.IISOutOfProcess;

            var arguments = benchmarksDll;

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

                arguments = "";
            }

            job.BasePath = workingDirectory;

            arguments += $" {job.Arguments}" +
                    $" --nonInteractive true" +
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

            if (iis)
            {
                Log.WriteLine($"Generating application host config for '{executable} {arguments}'");

                var apphost = GenerateApplicationHostConfig(job, "published", executable, arguments, hostname);
                arguments = $"-h \"{apphost}\"";
                executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\inetsrv\w3wp.exe");
            }
            else
            {
                arguments += $" --server.urls {serverUrl}";
            }

            Log.WriteLine($"Invoking executable: {executable}, with arguments: {arguments}");
            var process = new Process()
            {
                StartInfo = {
                    FileName = executable,
                    Arguments = arguments,
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
                        MarkAsRunning(hostname, benchmarksRepo, job, stopwatch, process);
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

            if (iis)
            {
                await WaitToListen(job, hostname);
                MarkAsRunning(hostname, benchmarksRepo, job,  stopwatch, process);
            }

            return process;
        }

        private static void MarkAsRunning(string hostname, string benchmarksRepo, ServerJob job, Stopwatch stopwatch,
            Process process)
        {
            job.StartupMainMethod = stopwatch.Elapsed;

            Log.WriteLine($"Running job '{job.Id}' with scenario '{job.Scenario}'");
            job.Url = ComputeServerUrl(hostname, job);

            // Mark the job as running to allow the Client to start the test
            job.State = ServerState.Running;
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
