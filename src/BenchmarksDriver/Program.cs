// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Benchmarks.ClientJob;
using Benchmarks.ServerJob;
using BenchmarksDriver.Ignore;
using BenchmarksDriver.Serializers;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BenchmarksDriver
{
    public class Program
    {
        private static bool _verbose;
        private static bool _quiet;
        private static bool _displayOutput;
        private static TimeSpan _timeout = TimeSpan.FromMinutes(5);

        private static readonly HttpClient _httpClient;
        private static readonly HttpClientHandler _httpClientHandler;

        private static ClientJob _clientJob;
        private static string _tableName = "AspNetBenchmarks";
        private const string EventPipeOutputFile = "eventpipe.netperf";
        private static string EventPipeConfig = "Microsoft-DotNETCore-SampleProfiler:FFFF:5,Microsoft-Windows-DotNETRuntime:4c14fccbd:5";

        // Default to arguments which should be sufficient for collecting trace of default Plaintext run
        private const string _defaultTraceArguments = "BufferSizeMB=1024;CircularMB=1024;clrEvents=JITSymbols;kernelEvents=process+thread+ImageLoad+Profile";
        private static List<string> _temporaryFolders = new List<string>();

        private static CommandOption
            _outputArchiveOption,
            _initializeOption,
            _cleanOption,
            _memoryLimitOption,
            _enableEventPipeOption,
            _eventPipeArgumentsOption,
            _initSubmodulesOption,
            _branchOption,
            _hashOption,
            _noGlobalJsonOption,
            _collectCountersOption,
            _noStartupLatencyOption
            ;

        private static Dictionary<string, string> _deprecatedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "--projectfile", "--project-file" },
            { "--outputfile", "--output-file" },
        };

        private static Dictionary<string, string> _synonymArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "--aspnet", "--aspnetcoreversion" },
            { "--runtime", "--runtimeversion" },
        };

        public static CounterProfile[] Counters = new CounterProfile[]
        {
            new CounterProfile{ Name="cpu-usage", Description="Amount of time the process has utilized the CPU (ms)", DisplayName="CPU Usage (%)", Format="", Compute = x => x.Max() },
            new CounterProfile{ Name="working-set", Description="Amount of working set used by the process (MB)", DisplayName="Working Set (MB)", Format="", Compute = x => x.Max()  },
            new CounterProfile{ Name="gc-heap-size", Description="Total heap size reported by the GC (MB)", DisplayName="GC Heap Size (MB)", Format="n0", Compute = Percentile(50)  },
            new CounterProfile{ Name="gen-0-gc-count", Description="Number of Gen 0 GCs / sec", DisplayName="Gen 0 GC (#/s)", Format="n0", Compute = x => x.Average()  },
            new CounterProfile{ Name="gen-1-gc-count", Description="Number of Gen 1 GCs / sec", DisplayName="Gen 1 GC (#/s)", Format="n0", Compute = x => x.Average()  },
            new CounterProfile{ Name="gen-2-gc-count", Description="Number of Gen 2 GCs / sec", DisplayName="Gen 2 GC (#/s)", Format="n0", Compute = x => x.Average()  },
            new CounterProfile{ Name="time-in-gc", Description="% time in GC since the last GC", DisplayName="Time in GC (%)", Format="n0", Compute = x => x.Average()  },
            new CounterProfile{ Name="gen-0-size", Description="Gen 0 Heap Size", DisplayName="Gen 0 Size (B)", Format="n0", Compute = Percentile(50)  },
            new CounterProfile{ Name="gen-1-size", Description="Gen 1 Heap Size", DisplayName="Gen 1 Size (B)", Format="n0", Compute = Percentile(50)  },
            new CounterProfile{ Name="gen-2-size", Description="Gen 2 Heap Size", DisplayName="Gen 2 Size (B)", Format="n0", Compute = Percentile(50)  },
            new CounterProfile{ Name="loh-size", Description="LOH Heap Size", DisplayName="LOH Size (B)", Format="n0", Compute = Percentile(50)  },
            new CounterProfile{ Name="alloc-rate", Description="Allocation Rate", DisplayName="Allocation Rate (B/sec)", Format="n0", Compute = x => x.Average()  },
            new CounterProfile{ Name="assembly-count", Description="Number of Assemblies Loaded", DisplayName="# of Assemblies Loaded", Format="n0", Compute = x => x.Max()  },
            new CounterProfile{ Name="exception-count", Description="Number of Exceptions / sec", DisplayName="Exceptions (#/s)", Format="n0", Compute = x => x.Average()  },
            new CounterProfile{ Name="threadpool-thread-count", Description="Number of ThreadPool Threads", DisplayName="ThreadPool Threads Count", Format="n0", Compute = Percentile(50)  },
            new CounterProfile{ Name="monitor-lock-contention-count", Description="Monitor Lock Contention Count", DisplayName="Lock Contention (#/s)", Format="n0", Compute = x => x.Average()  },
            new CounterProfile{ Name="threadpool-queue-length", Description="ThreadPool Work Items Queue Length", DisplayName="ThreadPool Queue Length", Format="n0", Compute = Percentile(50)  },
            new CounterProfile{ Name="threadpool-completed-items-count", Description="ThreadPool Completed Work Items Count", DisplayName="ThreadPool Items (#/s)", Format="n0", Compute = x => x.Average()  },
        };
        static Program()
        {
            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClientHandler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;

            _httpClient = new HttpClient(_httpClientHandler);
        }

        public static int Main(string[] args)
        {
            // Replace deprecated arguments with new ones
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (_deprecatedArguments.TryGetValue(arg, out var mappedArg))
                {
                    Log($"WARNING: '{arg}' has been deprecated, in the future please use '{mappedArg}'.");
                    args[i] = mappedArg;
                }
                else if (_synonymArguments.TryGetValue(arg, out var synonymArg))
                {
                    // We don't need to display a warning
                    args[i] = synonymArg;
                }
            }

            var app = new CommandLineApplication()
            {
                Name = "BenchmarksDriver",
                FullName = "ASP.NET Benchmark Driver",
                Description = "Driver for ASP.NET Benchmarks",
                ResponseFileHandling = ResponseFileHandling.ParseArgsAsSpaceSeparated,
                OptionsComparison = StringComparison.OrdinalIgnoreCase
            };

            app.HelpOption("-?|-h|--help");

            // Driver Options
            var clientOption = app.Option("-c|--client",
                "URL of benchmark client", CommandOptionType.MultipleValue);
            var clientNameOption = app.Option("--clientName",
                "Name of client to use for testing, e.g. Wrk", CommandOptionType.SingleValue);
            var serverOption = app.Option("-s|--server",
                "URL of benchmark server", CommandOptionType.SingleValue);
            var sqlConnectionStringOption = app.Option("-q|--sql",
                "Connection string of SQL Database to store results", CommandOptionType.SingleValue);
            var sqlTableOption = app.Option("-t|--table",
                "Table name of the SQL Database to store results", CommandOptionType.SingleValue);
            var verboseOption = app.Option("-v|--verbose",
                "Verbose output", CommandOptionType.NoValue);
            var quietOption = app.Option("--quiet",
                "Quiet output, only the results are displayed", CommandOptionType.NoValue);
            var sessionOption = app.Option("--session",
                "A logical identifier to group related jobs.", CommandOptionType.SingleValue);
            var descriptionOption = app.Option("--description",
                "The description of the job.", CommandOptionType.SingleValue);
            var iterationsOption = app.Option("-i|--iterations",
                "The number of iterations.", CommandOptionType.SingleValue);
            var excludeOption = app.Option("-x|--exclude",
                "The number of best and worst jobs to skip.", CommandOptionType.SingleValue);
            var shutdownOption = app.Option("--before-shutdown",
                "An endpoint to call before the application has shut down.", CommandOptionType.SingleValue);
            var spanOption = app.Option("-sp|--span",
                "The time during which the client jobs are repeated, in 'HH:mm:ss' format. e.g., 48:00:00 for 2 days.", CommandOptionType.SingleValue);
            var markdownOption = app.Option("-md|--markdown",
                "Formats the output in markdown", CommandOptionType.NoValue);
            var writeToFileOption = app.Option("-wf|--write-file",
                "Writes the results to a file", CommandOptionType.NoValue);
            var windowsOnlyOption = app.Option("--windows-only",
                "Don't execute the job if the server is not running on Windows", CommandOptionType.NoValue);
            var linuxOnlyOption = app.Option("--linux-only",
                "Don't execute the job if the server is not running on Linux", CommandOptionType.NoValue);
            var saveOption = app.Option("--save",
                "Stores the results in a local file, e.g. --save baseline. If the extension is not specified, '.bench.json' is used.", CommandOptionType.SingleValue);
            var diffOption = app.Option("--diff",
                "Displays the results of the run compared to a previously saved result, e.g. --diff baseline. If the extension is not specified, '.bench.json' is used.", CommandOptionType.SingleValue);
            var displayOutputOption = app.Option("--display-output",
                "Displays the standard output from the server job.", CommandOptionType.NoValue);
            var benchmarkdotnetOption = app.Option("--benchmarkdotnet",
                "Runs a BenchmarkDotNet application, with an optional filter. e.g., --benchmarkdotnet, --benchmarkdotnet:*MyBenchmark*", CommandOptionType.SingleOrNoValue);
            var consoleOption = app.Option("--console",
                "Runs the benchmarked application as a console application, such that no client is used and its output is displayed locally.", CommandOptionType.NoValue);

            // ServerJob Options
            var databaseOption = app.Option("--database",
                "The type of database to run the benchmarks with (PostgreSql, SqlServer or MySql). Default is None.", CommandOptionType.SingleValue);
            var kestrelThreadCountOption = app.Option("--kestrelThreadCount",
                "Maps to KestrelServerOptions.ThreadCount.",
                CommandOptionType.SingleValue);
            var scenarioOption = app.Option("-n|--scenario",
                "Benchmark scenario to run", CommandOptionType.SingleValue);
            var schemeOption = app.Option("-m|--scheme",
                "Scheme (http, https, h2, h2c). Default is http.", CommandOptionType.SingleValue);
            var webHostOption = app.Option(
                "-w|--webHost",
                "WebHost (e.g., KestrelLibuv, KestrelSockets, HttpSys). Default is KestrelSockets.",
                CommandOptionType.SingleValue);
            var aspnetCoreVersionOption = app.Option("-aspnet|--aspnetCoreVersion",
                "ASP.NET Core packages version (Current, Latest, or custom value). Current is the latest public version (2.0.*), Latest is the currently developed one. Default is Latest (2.2-*).", CommandOptionType.SingleValue);
            var runtimeVersionOption = app.Option("-dotnet|--runtimeVersion",
                ".NET Core Runtime version (Current, Latest, Edge or custom value). Current is the latest public version, Latest is the one enlisted, Edge is the latest available. Default is Latest (2.2.0-*).", CommandOptionType.SingleValue);
            var argOption = app.Option("-a|--arg",
                "Argument to pass to the application. (e.g., --arg \"--raw=true\" --arg \"single_value\")", CommandOptionType.MultipleValue);
            var noArgumentsOptions = app.Option("--no-arguments",
                "Removes any predefined arguments from the server application command line.", CommandOptionType.NoValue);
            var portOption = app.Option("--port",
                "The port used to request the benchmarked application. Default is 5000.", CommandOptionType.SingleValue);
            var readyTextOption = app.Option("--ready-text",
                "The text that is displayed when the application is ready to accept requests. (e.g., \"Application started.\")", CommandOptionType.SingleValue);
            var repositoryOption = app.Option("-r|--repository",
                "Git repository containing the project to test.", CommandOptionType.SingleValue);
            _branchOption = app.Option("-b|--branch",
                "Git repository containing the project to test.", CommandOptionType.SingleValue);
            _hashOption = app.Option("-h|--hash",
                "Git repository containing the project to test.", CommandOptionType.SingleValue);
            var sourceOption = app.Option("-src|--source",
                "Local folder containing the project to test.", CommandOptionType.SingleValue);
            var dockerFileOption = app.Option("-df|--docker-file",
                "File path of the Docker script. (e.g, \"frameworks/CSharp/aspnetcore/aspcore.dockerfile\")", CommandOptionType.SingleValue);
            var dockerContextOption = app.Option("-dc|--docker-context",
                "Docker context directory. Defaults to the Docker file directory. (e.g., \"frameworks/CSharp/aspnetcore/\")", CommandOptionType.SingleValue);
            var dockerImageOption = app.Option("-di|--docker-image",
                "The name of the Docker image to create. If not net one will be created from the Docker file name. (e.g., \"aspnetcore21\")", CommandOptionType.SingleValue);
            var projectOption = app.Option("--project-file",
                "Relative path of the project to test in the repository. (e.g., \"src/Benchmarks/Benchmarks.csproj)\"", CommandOptionType.SingleValue);
            _initSubmodulesOption = app.Option("--init-submodules",
                "When set will init submodules on the repository.", CommandOptionType.NoValue);
            var useRuntimeStoreOption = app.Option("--runtime-store",
                "Runs the benchmarks using the runtime store (2.0) or shared aspnet framework (2.1).", CommandOptionType.NoValue);
            var selfContainedOption = app.Option("--self-contained",
                "Publishes the .NET Core runtime with the application.", CommandOptionType.NoValue);
            var outputFileOption = app.Option("--output-file",
                "Output file attachment. Format is 'path[;destination]'. FilePath can be a URL. e.g., " +
                "\"--output-file c:\\build\\Microsoft.AspNetCore.Mvc.dll\", " +
                "\"--output-file c:\\files\\samples\\picture.png;wwwroot\\picture.png\"",
                CommandOptionType.MultipleValue);
            _outputArchiveOption = app.Option("--output-archive",
                "Output archive attachment. Format is 'path[;destination]'. FilePath can be a URL. e.g., " +
                "\"--output-archive c:\\build\\Microsoft.AspNetCore.Mvc.zip\", " +
                "\"--output-archive http://raw/github.com/pictures.zip;wwwroot\\pictures\"", 
                CommandOptionType.MultipleValue);
            var scriptFileOption = app.Option("--script",
                "WRK script path. File path can be a URL. e.g., " +
                "\"--script c:\\scripts\\post.lua\"",
                CommandOptionType.MultipleValue);
            var collectTraceOption = app.Option("--collect-trace",
                "Collect a PerfView trace.", CommandOptionType.NoValue);
            var collectStartup = app.Option("--collect-startup",
                "Includes the startup phase in the trace.", CommandOptionType.NoValue);
            _collectCountersOption = app.Option("--collect-counters",
                "Collect event counters.", CommandOptionType.NoValue);
            _enableEventPipeOption = app.Option("--enable-eventpipe",
                "Enables EventPipe perf collection.", CommandOptionType.NoValue);
            _eventPipeArgumentsOption = app.Option("--eventpipe-arguments",
                $"EventPipe configuration. Defaults to \"{EventPipeConfig}\"", CommandOptionType.SingleValue);
            var traceArgumentsOption = app.Option("--trace-arguments",
                $"Arguments used when collecting a PerfView trace. Defaults to \"{_defaultTraceArguments}\".",
                CommandOptionType.SingleValue);
            var traceOutputOption = app.Option("--trace-output",
                @"Can be a file prefix (app will add *.DATE.RPS*.etl.zip) , or a specific name (end in *.etl.zip) and no DATE.RPS* will be added e.g. --trace-output c:\traces\myTrace", CommandOptionType.SingleValue);
            var disableR2ROption = app.Option("--no-crossgen",
                "Disables Ready To Run (aka crossgen), in order to use the JITed version of the assemblies.", CommandOptionType.NoValue);
            var tieredCompilationOption = app.Option("--tiered-compilation",
                "Enables tiered-compilation.", CommandOptionType.NoValue);
            var collectR2RLogOption = app.Option("--collect-crossgen",
                "Download the Ready To Run log.", CommandOptionType.NoValue);
            var environmentVariablesOption = app.Option("-e|--env",
                "Defines custom environment variables to use with the benchmarked application e.g., -e \"KEY=VALUE\" -e \"A=B\"", CommandOptionType.MultipleValue);
            var buildArguments = app.Option("-ba|--build-arg",
                "Defines custom build arguments to use with the benchmarked application e.g., -b \"/p:foo=bar\" --build-arg \"quiet\"", CommandOptionType.MultipleValue);
            var downloadFilesOption = app.Option("-d|--download",
                "Downloads specific server files. This argument can be used multiple times. e.g., -d \"published/wwwroot/picture.png\"", CommandOptionType.MultipleValue);
            var noCleanOption = app.Option("--no-clean",
                "Don't delete the application on the server.", CommandOptionType.NoValue);
            var fetchOption = app.Option("--fetch",
                "Downloads the published application locally.", CommandOptionType.NoValue);
            var fetchOutputOption = app.Option("--fetch-output",
                @"Can be a file prefix (app will add *.DATE*.zip) , or a specific name (end in *.zip) and no DATE* will be added e.g. --fetch-output c:\publishedapps\myApp", CommandOptionType.SingleValue);
            var serverTimeoutOption = app.Option("--server-timeout",
                "Timeout for server jobs. e.g., 00:05:00", CommandOptionType.SingleValue);
            var frameworkOption = app.Option("--framework",
                "TFM to use if automatic resolution based runtime should not be used. e.g., netcoreapp2.1", CommandOptionType.SingleValue);
            var sdkOption = app.Option("--sdk",
                "SDK version to use", CommandOptionType.SingleValue);
            _noGlobalJsonOption = app.Option("--no-global-json",
                "Doesn't generate global.json", CommandOptionType.NoValue);
            _initializeOption = app.Option("--initialize",
                "A script to run before the application starts, e.g. \"du\", \"/usr/bin/env bash dotnet-install.sh\"", CommandOptionType.SingleValue);
            _cleanOption = app.Option("--clean",
                "A script to run after the application has stopped, e.g. \"du\", \"/usr/bin/env bash dotnet-install.sh\"", CommandOptionType.SingleValue);
            _memoryLimitOption = app.Option("-mem|--memory",
                "The amount of memory available for the process, e.g. -mem 64mb, -mem 1gb. Supported units are (gb, mb, kb, b or none for bytes).", CommandOptionType.SingleValue);

            // ClientJob Options
            var clientThreadsOption = app.Option("--client-threads",
                "Number of threads used by client. Default is 32.", CommandOptionType.SingleValue);
            var clientTimeoutOption = app.Option("--client-timeout",
                "Timeout for client connections. e.g., 2s", CommandOptionType.SingleValue);
            var connectionsOption = app.Option("--connections",
                "Number of connections used by client. Default is 256.", CommandOptionType.SingleValue);
            var durationOption = app.Option("--duration",
                "Duration of client job in seconds. Default is 15.", CommandOptionType.SingleValue);
            var warmupOption = app.Option("--warmup",
                "Duration of warmup in seconds. Default is 15. 0 disables the warmup and is equivalent to --no-warmup.", CommandOptionType.SingleValue);
            var noWarmupOption = app.Option("--no-warmup",
                "Disables the warmup phase.", CommandOptionType.NoValue);
            var headerOption = app.Option("--header",
                "Header added to request.", CommandOptionType.MultipleValue);
            var headersOption = app.Option("--headers",
                "Default set of HTTP headers added to request (None, Plaintext, Json, Html). Default is Html.", CommandOptionType.SingleValue);
            var methodOption = app.Option("--method",
                "HTTP method of the request. Default is GET.", CommandOptionType.SingleValue);
            var clientProperties = app.Option("-p|--properties",
                "Key value pairs of properties specific to the client running. e.g., -p ScriptName=pipeline -p PipelineDepth=16", CommandOptionType.MultipleValue);
            var pathOption = app.Option(
                "--path",
                "Relative URL where the client should send requests.",
                CommandOptionType.SingleValue);
            var querystringOption = app.Option(
                "--querystring",
                "Querystring to add to the requests. (e.g., \"?page=1\")",
                CommandOptionType.SingleValue);
            var jobsOptions = app.Option("-j|--jobs",
                "The path or url to the jobs definition.", CommandOptionType.SingleValue);
            _noStartupLatencyOption = app.Option("-nsl|--no-startup-latency",
                "Skip startup latency measurement.", CommandOptionType.NoValue);

            #region Switching console mode on Windows

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var iStdOut = GetStdHandle(STD_OUTPUT_HANDLE);
                if (!GetConsoleMode(iStdOut, out uint outConsoleMode))
                {
                    Console.WriteLine("failed to get output console mode");
                }

                outConsoleMode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN;
                if (!SetConsoleMode(iStdOut, outConsoleMode))
                {
                    Console.WriteLine($"failed to set output console mode, error code: {GetLastError()}");
                }
            }

            #endregion
            app.OnExecute(() =>
            {
                _verbose = verboseOption.HasValue();
                _quiet = quietOption.HasValue();
                _displayOutput = displayOutputOption.HasValue();

                if (serverTimeoutOption.HasValue())
                {
                    TimeSpan.TryParse(serverTimeoutOption.Value(), out _timeout);
                }

                var schemeValue = schemeOption.Value();
                if (string.IsNullOrEmpty(schemeValue))
                {
                    schemeValue = "http";
                }

                var webHostValue = webHostOption.Value();
                if (string.IsNullOrEmpty(webHostValue))
                {
                    webHostValue = "KestrelSockets";
                }

                var session = sessionOption.Value();
                if (String.IsNullOrEmpty(session))
                {
                    session = Guid.NewGuid().ToString("n");
                }

                var description = descriptionOption.Value() ?? "";

                if (iterationsOption.HasValue() && spanOption.HasValue())
                {
                    Console.WriteLine($"The options --iterations and --span can't be used together.");

                    app.ShowHelp();
                    return 10;
                }

                var server = serverOption.Value();
                var clients = clientOption.Values;
                var headers = Headers.Html;
                var jobDefinitionPathOrUrl = jobsOptions.Value();
                var iterations = 1;
                var exclude = 0;

                var sqlConnectionString = sqlConnectionStringOption.Value();
                TimeSpan span = TimeSpan.Zero;

                if (!Enum.TryParse(schemeValue, ignoreCase: true, result: out Scheme scheme) ||
                    !Enum.TryParse(webHostValue, ignoreCase: true, result: out WebHost webHost) ||
                    (headersOption.HasValue() && !Enum.TryParse(headersOption.Value(), ignoreCase: true, result: out headers)) ||
                    (databaseOption.HasValue() && !Enum.TryParse(databaseOption.Value(), ignoreCase: true, result: out Database database)) ||
                    string.IsNullOrWhiteSpace(server) ||
                    (clients.Any(client => string.IsNullOrWhiteSpace(client)) && !(benchmarkdotnetOption.HasValue() || consoleOption.HasValue())) ||
                    (spanOption.HasValue() && !TimeSpan.TryParse(spanOption.Value(), result: out span)) ||
                    (iterationsOption.HasValue() && !int.TryParse(iterationsOption.Value(), result: out iterations)) ||
                    (excludeOption.HasValue() && !int.TryParse(excludeOption.Value(), result: out exclude)))
                {
                    app.ShowHelp();
                    return 2;
                }

                if (sqlTableOption.HasValue())
                {
                    _tableName = sqlTableOption.Value();
                }

                var scenarioName = scenarioOption.Value() ?? "Default";
                JobDefinition jobDefinitions;

                if (!string.IsNullOrWhiteSpace(jobDefinitionPathOrUrl))
                {
                    string jobDefinitionContent;

                    // Load the job definition from a url or locally
                    try
                    {
                        if (jobDefinitionPathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            jobDefinitionContent = _httpClient.GetStringAsync(jobDefinitionPathOrUrl).GetAwaiter().GetResult();
                        }
                        else
                        {
                            jobDefinitionContent = File.ReadAllText(jobDefinitionPathOrUrl);
                        }
                    }
                    catch
                    {
                        Console.WriteLine($"Job definition '{jobDefinitionPathOrUrl}' could not be loaded.");
                        return 7;
                    }

                    jobDefinitions = JsonConvert.DeserializeObject<JobDefinition>(jobDefinitionContent);

                    if (!jobDefinitions.ContainsKey(scenarioName))
                    {
                        if (scenarioName == "Default")
                        {
                            Console.WriteLine($"Default job not found in the job definition file.");
                        }
                        else
                        {
                            Console.WriteLine($"Job named '{scenarioName}' not found in the job definition file.");
                        }

                        return 7;
                    }
                    else
                    {
                        // Normalizes the scenario name by using the one from the job definition
                        scenarioName = jobDefinitions.First(x => String.Equals(x.Key, scenarioName, StringComparison.OrdinalIgnoreCase)).Key;
                    }
                }
                else
                {
                    if (!scenarioOption.HasValue())
                    {
                        scenarioName = "Default";
                    }

                    if ((!(repositoryOption.HasValue() || sourceOption.HasValue()) ||
                        !projectOption.HasValue()) &&
                        !dockerFileOption.HasValue())
                    {
                        Console.WriteLine($"Repository or source folder and project are mandatory when no job definition is specified.");
                        return 9;
                    }

                    jobDefinitions = new JobDefinition();
                    jobDefinitions.Add(scenarioName, new JObject());
                }

                var mergeOptions = new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge };

                if (!jobDefinitions.TryGetValue("Default", out var defaultJob))
                {
                    defaultJob = new JObject();
                }

                var job = jobDefinitions[scenarioName];

                // Building ServerJob

                var mergedServerJob = new JObject(defaultJob);
                mergedServerJob.Merge(job);
                var serverJob = mergedServerJob.ToObject<ServerJob>();
                var jobOptions = mergedServerJob.ToObject<JobOptions>();

                if (pathOption.HasValue() && jobOptions.Paths != null && jobOptions.Paths.Count > 0)
                {
                    jobOptions.Paths.Add(serverJob.Path);

                    if (!jobOptions.Paths.Any(p => string.Equals(p, serverJob.Path, StringComparison.OrdinalIgnoreCase)) &&
                        !jobOptions.Paths.Any(p => string.Equals(p, "/" + serverJob.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        Console.WriteLine($"Scenario '{scenarioName}' does not support {pathOption.LongName} '{pathOption.Value()}'. Choose from:");
                        Console.WriteLine($"'{string.Join("', '", jobOptions.Paths)}'");
                        return 6;
                    }
                }

                // If the KnownHeaders property of the job definition is a string, fetch it from the Headers enum
                // We only look at it no Preset is defined on the commandline
                if (!String.IsNullOrEmpty(jobOptions.PresetHeaders) && !headersOption.HasValue())
                {
                    if (!Enum.TryParse(jobOptions.PresetHeaders, ignoreCase: true, result: out headers))
                    {
                        Console.WriteLine($"Unknown KnownHeaders value: '{jobOptions.PresetHeaders}'. Choose from: None, Html, Json, Plaintext.");
                    }
                }

                // Scenario can't be set in job definitions
                serverJob.Scenario = scenarioName;
                serverJob.WebHost = webHost;

                if (_memoryLimitOption.HasValue())
                {
                    var memoryLimitValue = _memoryLimitOption.Value();

                    if (memoryLimitValue.EndsWith("mb", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ulong.TryParse(memoryLimitValue.Substring(0, memoryLimitValue.Length - 2), out var megaBytes))
                        {
                            serverJob.MemoryLimitInBytes = megaBytes * 1024 * 1024;
                        }
                        else
                        {
                            Console.WriteLine("Invalid memory limit value");
                            return -1;
                        }
                    }
                    else if (memoryLimitValue.EndsWith("gb", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ulong.TryParse(memoryLimitValue.Substring(0, memoryLimitValue.Length - 2), out var gigaBytes))
                        {
                            serverJob.MemoryLimitInBytes = gigaBytes * 1024 * 1024 * 1024;
                        }
                        else
                        {
                            Console.WriteLine("Invalid memory limit value");
                            return -1;
                        }
                    }
                    else if (memoryLimitValue.EndsWith("kb", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ulong.TryParse(memoryLimitValue.Substring(0, memoryLimitValue.Length - 2), out var kiloBytes))
                        {
                            serverJob.MemoryLimitInBytes = kiloBytes * 1024;
                        }
                        else
                        {
                            Console.WriteLine("Invalid memory limit value");
                            return -1;
                        }
                    }
                    else if (memoryLimitValue.EndsWith("b", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ulong.TryParse(memoryLimitValue.Substring(0, memoryLimitValue.Length - 1), out var bytes))
                        {
                            serverJob.MemoryLimitInBytes = bytes;
                        }
                        else
                        {
                            Console.WriteLine("Invalid memory limit value");
                            return -1;
                        }
                    }
                    else
                    {
                        if (ulong.TryParse(memoryLimitValue, out var bytes))
                        {
                            serverJob.MemoryLimitInBytes = bytes;
                        }
                        else
                        {
                            Console.WriteLine("Invalid memory limit value");
                            return -1;
                        }
                    }
                }
                if (_initializeOption.HasValue())
                {
                    serverJob.BeforeScript = _initializeOption.Value();
                }
                if (_cleanOption.HasValue())
                {
                    serverJob.AfterScript = _cleanOption.Value();
                }
                if (databaseOption.HasValue())
                {
                    serverJob.Database = Enum.Parse<Database>(databaseOption.Value(), ignoreCase: true);
                }
                if (pathOption.HasValue())
                {
                    serverJob.Path = pathOption.Value();
                }
                if (schemeOption.HasValue())
                {
                    serverJob.Scheme = scheme;
                }
                if (useRuntimeStoreOption.HasValue())
                {
                    serverJob.UseRuntimeStore = true;
                }
                if (selfContainedOption.HasValue())
                {
                    serverJob.SelfContained = true;
                }
                else
                {
                    if (outputFileOption.HasValue() || _outputArchiveOption.HasValue())
                    {
                        serverJob.SelfContained = true;

                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Log("WARNING: '--self-contained' has been set implicitly as custom local files are used.");
                        Console.ResetColor();
                    }
                    else if (aspnetCoreVersionOption.HasValue() || runtimeVersionOption.HasValue())
                    {
                        serverJob.SelfContained = true;

                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Log("WARNING: '--self-contained' has been set implicitly as custom runtime versions are used.");
                        Console.ResetColor();
                    }

                }
                if (kestrelThreadCountOption.HasValue())
                {
                    serverJob.KestrelThreadCount = int.Parse(kestrelThreadCountOption.Value());
                }
                if (noArgumentsOptions.HasValue())
                {
                    serverJob.NoArguments = true;
                }
                if (argOption.HasValue())
                {
                    serverJob.Arguments = serverJob.Arguments ?? "";

                    foreach (var arg in argOption.Values)
                    {
                        var equalSignIndex = arg.IndexOf('=');

                        if (equalSignIndex == -1)
                        {
                            serverJob.Arguments += " " + arg;
                        }
                        else
                        {
                            serverJob.Arguments += $" {arg.Substring(0, equalSignIndex)} {arg.Substring(equalSignIndex + 1)}";
                        }
                    }
                }
                if (portOption.HasValue())
                {
                    serverJob.Port = int.Parse(portOption.Value());
                }
                if (aspnetCoreVersionOption.HasValue())
                {
                    serverJob.AspNetCoreVersion = aspnetCoreVersionOption.Value();
                }
                if (runtimeVersionOption.HasValue())
                {
                    serverJob.RuntimeVersion = runtimeVersionOption.Value();
                }
                if (repositoryOption.HasValue())
                {
                    var source = repositoryOption.Value();
                    var sourceParts = source.Split('@', 2);
                    var repository = sourceParts[0];

                    if (sourceParts.Length > 1)
                    {
                        serverJob.Source.BranchOrCommit = sourceParts[1];
                    }

                    if (!repository.Contains(":"))
                    {
                        repository = $"https://github.com/aspnet/{repository}.git";
                    }

                    serverJob.Source.Repository = repository;
                }
                if (_branchOption.HasValue())
                {
                    serverJob.Source.BranchOrCommit = _branchOption.Value();
                }
                if (_hashOption.HasValue())
                {
                    serverJob.Source.BranchOrCommit = "#" + _hashOption.Value();
                }
                if (dockerFileOption.HasValue())
                {
                    serverJob.Source.DockerFile = dockerFileOption.Value();

                    if (dockerContextOption.HasValue())
                    {
                        serverJob.Source.DockerContextDirectory = dockerContextOption.Value();
                    }
                    else
                    {
                        serverJob.Source.DockerContextDirectory = Path.GetDirectoryName(serverJob.Source.DockerFile).Replace("\\", "/");
                    }

                    if (dockerImageOption.HasValue())
                    {
                        serverJob.Source.DockerImageName = dockerImageOption.Value();
                    }
                    else
                    {
                        serverJob.Source.DockerImageName = Path.GetFileNameWithoutExtension(serverJob.Source.DockerFile).Replace("-", "_").Replace("\\", "/").ToLowerInvariant();
                    }
                }
                if (_initSubmodulesOption.HasValue())
                {
                    serverJob.Source.InitSubmodules = true;
                }
                if (projectOption.HasValue())
                {
                    serverJob.Source.Project = projectOption.Value();
                }
                if (noCleanOption.HasValue())
                {
                    serverJob.NoClean = true;
                }
                if (frameworkOption.HasValue())
                {
                    serverJob.Framework = frameworkOption.Value();
                }
                if (sdkOption.HasValue())
                {
                    serverJob.SdkVersion = sdkOption.Value();
                }
                if (_noGlobalJsonOption.HasValue())
                {
                    serverJob.NoGlobalJson = true;
                }
                if (collectTraceOption.HasValue())
                {
                    serverJob.Collect = true;
                    serverJob.CollectArguments = _defaultTraceArguments;

                    if (traceArgumentsOption.HasValue())
                    {
                        var allDefaultArguments = ExpandTraceArguments(_defaultTraceArguments);
                        var allTraceArguments = ExpandTraceArguments(traceArgumentsOption.Value());

                        foreach (var item in allTraceArguments)
                        {
                            // null value to remove the argument
                            // empty value to keep the argument with no value, e.g. /GCCollectOnly
                            if (item.Value == null)
                            {
                                allDefaultArguments.Remove(item.Key);
                            }
                            else
                            {
                                allDefaultArguments[item.Key] = item.Value;
                            }
                        }

                        serverJob.CollectArguments = String.Join(";", allDefaultArguments.Select(x => $"{x.Key}={x.Value}"));
                    }
                    else
                    {
                        serverJob.CollectArguments = _defaultTraceArguments;
                    }
                }
                if (collectTraceOption.HasValue())
                {
                    serverJob.CollectStartup = true;
                }
                if (_collectCountersOption.HasValue())
                {
                    serverJob.CollectCounters = true;
                }
                if (_enableEventPipeOption.HasValue())
                {
                    // Enable Event Pipes
                    serverJob.EnvironmentVariables.Add("COMPlus_EnableEventPipe", "1");

                    // Set a specific name to find it more easily
                    serverJob.EnvironmentVariables.Add("COMPlus_EventPipeOutputFile", EventPipeOutputFile);

                    if (_eventPipeArgumentsOption.HasValue())
                    {
                        EventPipeConfig = _eventPipeArgumentsOption.Value();
                    }

                    // Default EventPipeConfig value
                    serverJob.EnvironmentVariables.Add("COMPlus_EventPipeConfig", EventPipeConfig);
                }
                if (disableR2ROption.HasValue())
                {
                    serverJob.EnvironmentVariables.Add("COMPlus_ReadyToRun", "0");
                }
                if (collectR2RLogOption.HasValue())
                {
                    serverJob.EnvironmentVariables.Add("COMPlus_ReadyToRunLogFile", "r2r");
                }
                if (tieredCompilationOption.HasValue())
                {
                    serverJob.EnvironmentVariables.Add("COMPlus_TieredCompilation", "1");
                }
                if (environmentVariablesOption.HasValue())
                {
                    foreach (var env in environmentVariablesOption.Values)
                    {
                        var index = env.IndexOf('=');

                        if (index == -1)
                        {
                            if (index == -1)
                            {
                                Console.WriteLine($"Invalid environment variable, '=' not found: '{env}'");
                                return 9;
                            }
                        }
                        else if (index == env.Length - 1)
                        {
                            serverJob.EnvironmentVariables[env.Substring(0, index)] = "";
                        }
                        else
                        {
                            serverJob.EnvironmentVariables[env.Substring(0, index)] = env.Substring(index + 1);
                        }
                    }
                }
                if (buildArguments.HasValue())
                {
                    foreach (var argument in buildArguments.Values)
                    {
                        serverJob.BuildArguments.Add(argument);
                    }
                }

                if (saveOption.HasValue())
                {
                    if (!descriptionOption.HasValue() || String.IsNullOrWhiteSpace(descriptionOption.Value()))
                    {
                        // Copy the --save value as the description
                        // It also means that if --diff and --save are used, --diff won't require a --description
                        descriptionOption = saveOption;
                    }
                }

                if (diffOption.HasValue() && (!descriptionOption.HasValue() || String.IsNullOrWhiteSpace(descriptionOption.Value())))
                {
                    Console.WriteLine($"The --description argument is mandatory when using --diff.");
                    return -1;
                }

                // Check all attachments exist
                if (outputFileOption.HasValue())
                {
                    foreach (var outputFile in outputFileOption.Values)
                    {
                        var fileSegments = outputFile.Split(';');
                        var filename = fileSegments[0];

                        if (!filename.Contains("*") && !filename.Contains("http") && !File.Exists(filename))
                        {
                            Console.WriteLine($"Output File '{filename}' could not be loaded.");
                            return 8;
                        }
                    }
                }

                // Check all scripts exist
                if (scriptFileOption.HasValue())
                {
                    foreach (var scriptFile in scriptFileOption.Values)
                    {
                        if (!File.Exists(scriptFile))
                        {
                            Console.WriteLine($"Script file '{scriptFile}' could not be loaded.");
                            return 8;
                        }
                    }
                }

                // Building ClientJob

                var mergedClientJob = new JObject(defaultJob);
                mergedClientJob.Merge(job);
                _clientJob = mergedClientJob.ToObject<ClientJob>();

                if (clientNameOption.HasValue())
                {
                    if (!Enum.TryParse<Worker>(clientNameOption.Value(), ignoreCase: true, result: out var worker))
                    {
                        Log($"Could not find worker {clientNameOption.Value()}");
                        return 9;
                    }

                    _clientJob.Client = worker;
                }

                if (benchmarkdotnetOption.HasValue())
                {
                    if (String.IsNullOrEmpty(serverJob.Scenario))
                    {
                        serverJob.Scenario = "Benchmark.NET";
                    }

                    serverJob.NoArguments = true;
                    _clientJob.Client = Worker.BenchmarkDotNet;

                    var bdnScenario = benchmarkdotnetOption.Value();
                    if (String.IsNullOrEmpty(bdnScenario))
                    {
                        bdnScenario = "*";
                    }

                    serverJob.Arguments += $" --inProcess --cli {{{{benchmarks-cli}}}} --filter {bdnScenario}";
                }

                if (consoleOption.HasValue())
                {
                    serverJob.IsConsoleApp = true;
                    _clientJob.Client = Worker.None;
                    serverJob.CollectStartup = true;
                }

                Log($"Using worker {_clientJob.Client}");

                if (_clientJob.Client == Worker.BenchmarkDotNet)
                {
                    serverJob.IsConsoleApp = true;
                    serverJob.ReadyStateText = "BenchmarkRunner: Start";
                    serverJob.CollectStartup = true;
                }

                // The ready state option overrides BenchmarDotNet's value
                if (readyTextOption.HasValue())
                {
                    serverJob.ReadyStateText = readyTextOption.Value();
                }

                // Override default ClientJob settings if options are set
                if (connectionsOption.HasValue())
                {
                    _clientJob.Connections = int.Parse(connectionsOption.Value());
                }
                if (clientThreadsOption.HasValue())
                {
                    _clientJob.Threads = int.Parse(clientThreadsOption.Value());
                }
                if (durationOption.HasValue())
                {
                    _clientJob.Duration = int.Parse(durationOption.Value());
                }
                if (warmupOption.HasValue())
                {
                    _clientJob.Warmup = int.Parse(warmupOption.Value());
                }
                if (noWarmupOption.HasValue())
                {
                    _clientJob.Warmup = 0;
                }
                if (clientProperties.HasValue())
                {
                    foreach (var property in clientProperties.Values)
                    {
                        var index = property.IndexOf('=');

                        if (index == -1)
                        {
                            Console.WriteLine($"Invalid property variable, '=' not found: '{property}'");
                            return 9;
                        }
                        else
                        {
                            _clientJob.ClientProperties[property.Substring(0, index)] = property.Substring(index + 1);
                        }
                    }
                }

                _clientJob.ClientProperties["protocol"] = schemeValue;

                if (methodOption.HasValue())
                {
                    _clientJob.Method = methodOption.Value();
                }
                if (querystringOption.HasValue())
                {
                    _clientJob.Query = querystringOption.Value();
                }
                if (span > TimeSpan.Zero)
                {
                    _clientJob.SpanId = Guid.NewGuid().ToString("n");
                }

                switch (headers)
                {
                    case Headers.None:
                        break;

                    case Headers.Html:
                        _clientJob.Headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
                        _clientJob.Headers["Connection"] = "keep-alive";
                        break;

                    case Headers.Json:
                        _clientJob.Headers["Accept"] = "application/json,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7";
                        _clientJob.Headers["Connection"] = "keep-alive";
                        break;

                    case Headers.Plaintext:
                        _clientJob.Headers["Accept"] = "text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7";
                        _clientJob.Headers["Connection"] = "keep-alive";
                        break;
                }

                if (headerOption.HasValue())
                {
                    foreach (var header in headerOption.Values)
                    {
                        var segments = header.Split('=', 2);

                        if (segments.Length != 2)
                        {
                            Console.WriteLine($"Invalid http header, '=' not found: '{header}'");
                            return 9;
                        }

                        _clientJob.Headers[segments[0].Trim()] = segments[1].Trim();
                    }
                }

                Benchmarks.ServerJob.OperatingSystem? requiredOperatingSystem = null;

                if (windowsOnlyOption.HasValue())
                {
                    requiredOperatingSystem = Benchmarks.ServerJob.OperatingSystem.Windows;
                }

                if (linuxOnlyOption.HasValue())
                {
                    requiredOperatingSystem = Benchmarks.ServerJob.OperatingSystem.Linux;
                }

                return Run(
                    new Uri(server),
                    clients.Select(client => new Uri(client)).ToArray(),
                    sqlConnectionString,
                    serverJob,
                    session,
                    description,
                    iterations,
                    exclude,
                    shutdownOption.Value(),
                    span,
                    downloadFilesOption.Values,
                    fetchOption.HasValue() || fetchOutputOption.HasValue(),
                    fetchOutputOption.Value(),
                    collectR2RLogOption.HasValue(),
                    traceOutputOption.Value(),
                    outputFileOption,
                    sourceOption,
                    scriptFileOption,
                    markdownOption,
                    writeToFileOption,
                    requiredOperatingSystem,
                    saveOption,
                    diffOption
                    ).Result;
            });

            // Resolve reponse files from urls

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("@http", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var tempFilename = Path.GetTempFileName();

                        var filecontent = _httpClient.GetStringAsync(args[i].Substring(1)).GetAwaiter().GetResult();
                        File.WriteAllText(tempFilename, filecontent);
                        args[i] = "@" + tempFilename;
                    }
                    catch
                    {
                        Console.WriteLine($"Invalid reponse file url '{args[i].Substring(1)}'");
                        return -1;
                    }
                }
            }

            try
            {
                return app.Execute(args);
            }
            catch (CommandParsingException e)
            {
                Console.WriteLine();
                Console.WriteLine(e.Message);
                return -1;
            }
            finally
            {
                CleanTemporaryFiles();
            }
        }

        private static async Task<int> Run(
            Uri serverUri,
            Uri[] clientUris,
            string sqlConnectionString,
            ServerJob serverJob,
            string session,
            string description,
            int iterations,
            int exclude,
            string shutdownEndpoint,
            TimeSpan span,
            List<string> downloadFiles,
            bool fetch,
            string fetchDestination,
            bool collectR2RLog,
            string traceDestination,
            CommandOption outputFileOption,
            CommandOption sourceOption,
            CommandOption scriptFileOption,
            CommandOption markdownOption,
            CommandOption writeToFileOption,
            Benchmarks.ServerJob.OperatingSystem? requiredOperatingSystem,
            CommandOption saveOption,
            CommandOption diffOption
            )
        {
            var scenario = serverJob.Scenario;
            var serverJobsUri = new Uri(serverUri, "/jobs");
            Uri serverJobUri = null;
            HttpResponseMessage response = null;
            string responseContent = null;

            var results = new List<Statistics>();
            ClientJob[] clientJobs = null;

            var serializer = WorkerFactory.CreateResultSerializer(_clientJob);

            if (serializer != null && !string.IsNullOrWhiteSpace(sqlConnectionString))
            {
                await serializer.InitializeDatabaseAsync(sqlConnectionString, _tableName);
            }

            var content = JsonConvert.SerializeObject(serverJob);

            Log($"Running session '{session}' with description '{description}'");

            for (var i = 1; i <= iterations; i++)
            {
                if (iterations > 1)
                {
                    Log($"Job {i} of {iterations}");
                }

                try
                {
                    Log($"Starting scenario {scenario} on benchmark server...");

                    LogVerbose($"POST {serverJobsUri} {content}...");

                    response = await _httpClient.PostAsync(serverJobsUri, new StringContent(content, Encoding.UTF8, "application/json"));
                    responseContent = await response.Content.ReadAsStringAsync();
                    LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");

                    response.EnsureSuccessStatusCode();

                    var retryCount = 0;

                    serverJobUri = new Uri(serverUri, response.Headers.Location);

                    Log($"Fetching job: {serverJobUri}");

                    while (true)
                    {
                        LogVerbose($"GET {serverJobUri}...");
                        response = await _httpClient.GetAsync(serverJobUri);
                        responseContent = await response.Content.ReadAsStringAsync();

                        serverJob = JsonConvert.DeserializeObject<ServerJob>(responseContent);

                        // Server might be busy and send a retry response
                        if (serverJob == null)
                        {
                            if (retryCount++ < 5)
                            {
                                Log($"Invalid response content detected {(int)response.StatusCode} ({response.StatusCode}), attempt {retryCount} ...");
                                continue;
                            }

                            Log(responseContent);
                            throw new InvalidOperationException("Invalid response from the server");
                        }

                        if (!serverJob.Hardware.HasValue)
                        {
                            throw new InvalidOperationException("Server is required to set ServerJob.Hardware.");
                        }

                        if (String.IsNullOrWhiteSpace(serverJob.HardwareVersion))
                        {
                            throw new InvalidOperationException("Server is required to set ServerJob.HardwareVersion.");
                        }

                        if (!serverJob.OperatingSystem.HasValue)
                        {
                            throw new InvalidOperationException("Server is required to set ServerJob.OperatingSystem.");
                        }

                        if (requiredOperatingSystem.HasValue && requiredOperatingSystem.Value != serverJob.OperatingSystem)
                        {
                            Log($"Job ignored on this OS, stopping job ...");

                            response = await _httpClient.PostAsync(serverJobUri + "/stop", new StringContent(""));
                            LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");

                            return 0;
                        }

                        if (serverJob?.State == ServerState.Initializing)
                        {
                            // Uploading source code
                            if (sourceOption.HasValue())
                            {
                                // Zipping the folder
                                var tempFilename = Path.GetTempFileName();
                                File.Delete(tempFilename);

                                Log("Zipping the source folder in " + tempFilename);

                                var sourceDir = sourceOption.Value();

                                if (!File.Exists(Path.Combine(sourceDir, ".gitignore")))
                                {
                                    ZipFile.CreateFromDirectory(sourceOption.Value(), tempFilename);
                                }
                                else
                                {
                                    LogVerbose(".gitignore file found");
                                    DoCreateFromDirectory(sourceDir, tempFilename);
                                }

                                var result = await UploadFileAsync(tempFilename, serverJob, serverJobUri + "/source");

                                File.Delete(tempFilename);

                                if (result != 0)
                                {
                                    return result;
                                }
                            }

                            // Upload custom package contents
                            if (_outputArchiveOption.HasValue())
                            {
                                foreach (var outputArchiveValue in _outputArchiveOption.Values)
                                {
                                    var outputFileSegments = outputArchiveValue.Split(';', 2, StringSplitOptions.RemoveEmptyEntries);

                                    string localArchiveFilename = outputFileSegments[0];

                                    var tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

                                    if (Directory.Exists(tempFolder))
                                    {
                                        Directory.Delete(tempFolder, true);
                                    }

                                    Directory.CreateDirectory(tempFolder);

                                    _temporaryFolders.Add(tempFolder);

                                    // Download the archive, while pinging the server to keep the job alive
                                    if (outputArchiveValue.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                    {
                                        localArchiveFilename = await DownloadTemporaryFileAsync(localArchiveFilename, serverJobUri);
                                    }

                                    ZipFile.ExtractToDirectory(localArchiveFilename, tempFolder);

                                    if (outputFileSegments.Length > 1)
                                    {
                                        outputFileOption.Values.Add(Path.Combine(tempFolder,"*.*") + ";" + outputFileSegments[1]);
                                    }
                                    else
                                    {
                                        outputFileOption.Values.Add(Path.Combine(tempFolder, "*.*"));
                                    }                                    
                                }
                            }

                            // Uploading attachments
                            if (outputFileOption.HasValue())
                            {
                                foreach (var outputFileValue in outputFileOption.Values)
                                {
                                    var outputFileSegments = outputFileValue.Split(';', 2, StringSplitOptions.RemoveEmptyEntries);

                                    foreach (var resolvedFile in Directory.GetFiles(Path.GetDirectoryName(outputFileSegments[0]), Path.GetFileName(outputFileSegments[0]), SearchOption.AllDirectories))
                                    {
                                        var resolvedFileWithDestination = resolvedFile;

                                        if (outputFileSegments.Length > 1)
                                        {
                                            resolvedFileWithDestination += ";" + outputFileSegments[1] + Path.GetDirectoryName(resolvedFile).Substring(Path.GetDirectoryName(outputFileSegments[0]).Length) + "/" + Path.GetFileName(resolvedFileWithDestination);
                                        }

                                        var result = await UploadFileAsync(resolvedFileWithDestination, serverJob, serverJobUri + "/attachment");

                                        if (result != 0)
                                        {
                                            return result;
                                        }
                                    }
                                }
                            }

                            response = await _httpClient.PostAsync(serverJobUri + "/start", new StringContent(""));
                            responseContent = await response.Content.ReadAsStringAsync();
                            LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");
                            response.EnsureSuccessStatusCode();

                            serverJob = JsonConvert.DeserializeObject<ServerJob>(responseContent);

                            Log($"Job submitted, waiting...");

                            break;
                        }
                        else
                        {
                            await Task.Delay(1000);
                        }
                    }

                    var serverBenchmarkUri = (string)null;
                    while (true)
                    {
                        var previousJob = serverJob;

                        LogVerbose($"GET {serverJobUri}...");
                        response = await _httpClient.GetAsync(serverJobUri);
                        responseContent = await response.Content.ReadAsStringAsync();

                        LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            return -1;
                        }

                        serverJob = JsonConvert.DeserializeObject<ServerJob>(responseContent);

                        if (!serverJob.Hardware.HasValue)
                        {
                            throw new InvalidOperationException("Server is required to set ServerJob.Hardware.");
                        }

                        if (String.IsNullOrWhiteSpace(serverJob.HardwareVersion))
                        {
                            throw new InvalidOperationException("Server is required to set ServerJob.HardwareVersion.");
                        }

                        if (!serverJob.OperatingSystem.HasValue)
                        {
                            throw new InvalidOperationException("Server is required to set ServerJob.OperatingSystem.");
                        }

                        if (serverJob.State == ServerState.Running)
                        {
                            if (previousJob.State == ServerState.Waiting)
                            {
                                Log($"Job acquired");
                            }

                            serverBenchmarkUri = serverJob.Url;
                            break;
                        }
                        else if (serverJob.State == ServerState.Failed)
                        {
                            Log($"Job failed on benchmark server, stopping...");

                            Log(serverJob.Error, notime: true, error: true);

                            // Returning will also send a Delete message to the server
                            return -1;
                        }
                        else if (serverJob.State == ServerState.NotSupported)
                        {
                            Log("Server does not support this job configuration.");
                            return 0;
                        }
                        else if (serverJob.State == ServerState.Stopped)
                        {
                            Log($"Job finished");

                            // If there is no ReadyStateText defined, the server will never fo in Running state
                            // and we'll reach the Stopped state eventually, but that's a normal behavior.
                            if (IsConsoleApp)
                            {
                                serverBenchmarkUri = serverJob.Url;
                                break;
                            }

                            return -1;
                        }
                        else
                        {
                            await Task.Delay(1000);
                        }
                    }

                    System.Threading.Thread.Sleep(200);  // Make it clear on traces when startup has finished and warmup begins.

                    TimeSpan latencyNoLoad = TimeSpan.Zero, latencyFirstRequest = TimeSpan.Zero;

                    // Reset this before each iteration
                    _clientJob.SkipStartupLatencies = _noStartupLatencyOption.HasValue();

                    if (!IsConsoleApp && _clientJob.Warmup != 0)
                    {
                        Log("Warmup");
                        var duration = _clientJob.Duration;

                        _clientJob.Duration = _clientJob.Warmup;

                        // Warmup using the first client
                        clientJobs = new [] { await RunClientJob(scenario, clientUris[0], serverJobUri, serverBenchmarkUri, scriptFileOption) };

                        // Store the latency as measured on the warmup job
                        // The first client is used to measure the latencies
                        latencyNoLoad = clientJobs[0].LatencyNoLoad;
                        latencyFirstRequest = clientJobs[0].LatencyFirstRequest;

                        _clientJob.Duration = duration;
                        System.Threading.Thread.Sleep(200);  // Make it clear on traces when warmup stops and measuring begins.
                    }

                    // Prevent the actual run from updating the startup statistics
                    _clientJob.SkipStartupLatencies = true;

                    var startTime = DateTime.UtcNow;
                    var spanLoop = 0;
                    var sqlTask = Task.CompletedTask;
                    string rpsStr = "";

                    do
                    {
                        if (span > TimeSpan.Zero)
                        {
                            Log($"Starting client job iteration {spanLoop}. Running since {startTime.ToLocalTime()}, with {((startTime + span) - DateTime.UtcNow):c} remaining.");

                            // Clear the measures from the server job and update it on the server
                            if (spanLoop > 0)
                            {
                                results.Clear();
                                response = await _httpClient.PostAsync(serverJobUri + "/resetstats", new StringContent(""));
                                response.EnsureSuccessStatusCode();
                            }
                        }

                        // Don't run the client job for None and BenchmarkDotNet
                        if (!IsConsoleApp)
                        {
                            var tasks = clientUris.Select(clientUri => RunClientJob(scenario, clientUri, serverJobUri, serverBenchmarkUri, scriptFileOption)).ToArray();
                            await Task.WhenAll(tasks);
                            clientJobs = tasks.Select(x => x.Result).ToArray();
                        }
                        else
                        {
                            // Don't wait for the client job as we are not starting it
                            clientJobs = new[] { new ClientJob { State = ClientState.Completed } };

                            // Wait until the server has stopped
                            var now = DateTime.UtcNow;

                            while (serverJob.State != ServerState.Stopped && (DateTime.UtcNow - now < _timeout))
                            {
                                // Load latest state of server job
                                LogVerbose($"GET {serverJobUri}...");

                                response = await _httpClient.GetAsync(serverJobUri);
                                response.EnsureSuccessStatusCode();
                                responseContent = await response.Content.ReadAsStringAsync();

                                LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

                                serverJob = JsonConvert.DeserializeObject<ServerJob>(responseContent);

                                await Task.Delay(1000);
                            }

                            if (serverJob.State == ServerState.Stopped)
                            {
                                // Try to extract BenchmarkDotNet statistics
                                if (_clientJob.Client == Worker.BenchmarkDotNet)
                                {
                                    await BenchmarkDotNetUtils.DownloadResultFiles(serverJobUri, _httpClient, (BenchmarkDotNetSerializer)serializer);
                                }
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.White;
                                Log($"Server job running for more than {_timeout}, stopping...");
                                Console.ResetColor();
                                serverJob.State = ServerState.Failed;
                            }

                        }

                        if (clientJobs.All(client => client.State == ClientState.Completed) && serverJob.State != ServerState.Failed)
                        {
                            LogVerbose($"Client Jobs completed");

                            if (span == TimeSpan.Zero && i == iterations && !String.IsNullOrEmpty(shutdownEndpoint))
                            {
                                Log($"Invoking '{shutdownEndpoint}' on benchmarked application...");
                                await InvokeApplicationEndpoint(serverJobUri, shutdownEndpoint);
                            }

                            // Load latest state of server job
                            LogVerbose($"GET {serverJobUri}...");

                            response = await _httpClient.GetAsync(serverJobUri);
                            response.EnsureSuccessStatusCode();
                            responseContent = await response.Content.ReadAsStringAsync();

                            LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

                            serverJob = JsonConvert.DeserializeObject<ServerJob>(responseContent);

                            // Download R2R log
                            if (collectR2RLog)
                            {
                                downloadFiles.Add("r2r." + serverJob.ProcessId);
                            }

                            if (clientJobs[0].Warmup == 0)
                            {
                                latencyNoLoad = clientJobs[0].LatencyNoLoad;
                                latencyFirstRequest = clientJobs[0].LatencyFirstRequest;
                            }

                            var serverCounters = serverJob.ServerCounters;
                            var workingSet = Math.Round(((double)serverCounters.Select(x => x.WorkingSet).DefaultIfEmpty(0).Max()) / (1024 * 1024), 3);
                            var cpu = serverCounters.Select(x => x.CpuPercentage).DefaultIfEmpty(0).Max();

                            var statistics = new Statistics
                            {
                                RequestsPerSecond = clientJobs.Sum(clientJob => clientJob.RequestsPerSecond),
                                LatencyOnLoad = clientJobs.Average(clientJob => clientJob.Latency.Average),
                                Cpu = cpu,
                                WorkingSet = workingSet,
                                StartupMain = serverJob.StartupMainMethod.TotalMilliseconds,
                                FirstRequest = latencyFirstRequest.TotalMilliseconds,
                                Latency = latencyNoLoad.TotalMilliseconds,
                                SocketErrors = clientJobs.Sum(clientJob => clientJob.SocketErrors),
                                BadResponses = clientJobs.Sum(clientJob => clientJob.BadResponses),

                                LatencyAverage = clientJobs.Average(clientJob => clientJob.Latency.Average),
                                Latency50Percentile = clientJobs.Average(clientJob => clientJob.Latency.Within50thPercentile),
                                Latency75Percentile = clientJobs.Average(clientJob => clientJob.Latency.Within75thPercentile),
                                Latency90Percentile = clientJobs.Average(clientJob => clientJob.Latency.Within90thPercentile),
                                Latency99Percentile = clientJobs.Average(clientJob => clientJob.Latency.Within99thPercentile),
                                MaxLatency = clientJobs.Average(clientJob => clientJob.Latency.MaxLatency),
                                TotalRequests = clientJobs.Sum(clientJob => clientJob.Requests),
                                Duration = clientJobs[0].ActualDuration.TotalMilliseconds
                            };

                            foreach (var entry in serverJob.Counters)
                            {
                                statistics.Other[entry.Key] = entry.Value.Select(x => double.Parse(x)).Max();
                                statistics.Samples[entry.Key] = entry.Value.Select(x => double.Parse(x)).ToArray();

                                var knownCounter = Counters.FirstOrDefault(x => x.Name == entry.Key);
                                if (knownCounter != null)
                                {
                                    statistics.Other[entry.Key] = knownCounter.Compute(entry.Value.Select(x => double.Parse(x)));
                                }
                            }

                            results.Add(statistics);

                            if (iterations > 1 && !IsConsoleApp)
                            {
                                LogVerbose($"RequestsPerSecond:           {statistics.RequestsPerSecond}");
                                LogVerbose($"Max CPU (%):                 {statistics.Cpu}");
                                LogVerbose($"WorkingSet (MB):             {statistics.WorkingSet}");
                                LogVerbose($"Latency (ms):                {statistics.Latency}");
                                LogVerbose($"Socket Errors:               {statistics.SocketErrors}");
                                LogVerbose($"Bad Responses:               {statistics.BadResponses}");

                                // Don't display these startup numbers on stress load
                                if (spanLoop == 0)
                                {
                                    LogVerbose($"Latency on load (ms):        {statistics.LatencyOnLoad}");
                                    LogVerbose($"Startup Main (ms):           {statistics.StartupMain}");
                                    LogVerbose($"First Request (ms):          {statistics.FirstRequest}");
                                }
                            }

                            if (String.IsNullOrEmpty(traceDestination))
                            {
                                traceDestination = "trace";
                            }

                            rpsStr = "RPS-" + ((int)((statistics.RequestsPerSecond + 500) / 1000)) + "K";

                            // EventPipe log
                            if (_enableEventPipeOption.HasValue())
                            {
                                Log($"EventPipe config: {EventPipeConfig}");
                            }

                            // Collect Trace
                            if (serverJob.Collect)
                            {
                                Log($"Post-processing profiler trace, this can take 10s of seconds...");

                                Log($"Trace arguments: {serverJob.CollectArguments}");

                                var uri = serverJobUri + "/trace";
                                response = await _httpClient.PostAsync(uri, new StringContent(""));
                                response.EnsureSuccessStatusCode();

                                while (true)
                                {
                                    LogVerbose($"GET {serverJobUri}...");
                                    response = await _httpClient.GetAsync(serverJobUri);
                                    responseContent = await response.Content.ReadAsStringAsync();

                                    LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

                                    if (response.StatusCode == HttpStatusCode.NotFound || String.IsNullOrEmpty(responseContent))
                                    {
                                        Log($"The job was forcibly stopped by the server.");
                                        return 1;
                                    }

                                    serverJob = JsonConvert.DeserializeObject<ServerJob>(responseContent);

                                    if (serverJob.State == ServerState.TraceCollected)
                                    {
                                        break;
                                    }
                                    else if (serverJob.State == ServerState.TraceCollecting)
                                    {
                                        // Server is collecting the trace
                                    }
                                    else
                                    {
                                        Log($"Unexpected state: {serverJob.State}");
                                    }

                                    await Task.Delay(1000);
                                }

                                var traceExtension = serverJob.OperatingSystem == Benchmarks.ServerJob.OperatingSystem.Windows
                                    ? ".etl.zip"
                                    : ".trace.zip";

                                var traceOutputFileName = traceDestination;
                                if (traceOutputFileName == null || !traceOutputFileName.EndsWith(traceExtension, StringComparison.OrdinalIgnoreCase))
                                {
                                    traceOutputFileName = traceOutputFileName + "." + DateTime.Now.ToString("MM-dd-HH-mm-ss") + "." + rpsStr + traceExtension;
                                }

                                try
                                {
                                    Log($"Downloading trace: {traceOutputFileName}");
                                    await _httpClient.DownloadFileAsync(uri, serverJobUri, traceOutputFileName);
                                }
                                catch (HttpRequestException)
                                {
                                    Log($"FAILED: The trace was not successful");
                                }
                            }

                            var shouldComputeResults = results.Any() && iterations == i && !IsConsoleApp;

                            Statistics average = null;

                            if (shouldComputeResults)
                            {
                                var samples = results.OrderBy(x => x.RequestsPerSecond).Skip(exclude).SkipLast(exclude).ToList();

                                average = new Statistics
                                {
                                    Description = description,

                                    RequestsPerSecond = Math.Round(samples.Average(x => x.RequestsPerSecond)),
                                    LatencyOnLoad = Math.Round(samples.Average(x => x.LatencyOnLoad), 2),
                                    Cpu = Math.Round(samples.Average(x => x.Cpu)),
                                    WorkingSet = Math.Round(samples.Average(x => x.WorkingSet)),
                                    StartupMain = Math.Round(samples.Average(x => x.StartupMain)),
                                    FirstRequest = Math.Round(samples.Average(x => x.FirstRequest), 2),
                                    SocketErrors = Math.Round(samples.Average(x => x.SocketErrors)),
                                    BadResponses = Math.Round(samples.Average(x => x.BadResponses)),

                                    Latency = Math.Round(samples.Average(x => x.Latency), 2),
                                    LatencyAverage = Math.Round(samples.Average(x => x.LatencyAverage), 2),
                                    Latency50Percentile = Math.Round(samples.Average(x => x.Latency50Percentile), 2),
                                    Latency75Percentile = Math.Round(samples.Average(x => x.Latency75Percentile), 2),
                                    Latency90Percentile = Math.Round(samples.Average(x => x.Latency90Percentile), 2),
                                    Latency99Percentile = Math.Round(samples.Average(x => x.Latency99Percentile), 2),
                                    MaxLatency = Math.Round(samples.Average(x => x.MaxLatency), 2),
                                    TotalRequests = Math.Round(samples.Average(x => x.TotalRequests)),
                                    Duration = Math.Round(samples.Average(x => x.Duration))
                                };

                                foreach (var counter in statistics.Other.Keys)
                                {
                                    average.Other[counter] = samples.Average(x => x.Other[counter]);
                                    average.Samples[counter] = samples.Last().Samples[counter];
                                }

                                if (serializer != null)
                                {
                                    serializer.ComputeAverages(average, samples);
                                }

                                var fields = BuildFields(average);

                                var header = new StringBuilder();
                                var separator = new StringBuilder();
                                var values = new StringBuilder();

                                // Headers
                                foreach (var field in fields)
                                {
                                    var size = Math.Max(field.Key.Length, field.Value.Length);
                                    header.Append("| ").Append(field.Key.PadLeft(size)).Append(" ");
                                    separator.Append("| ").Append(new String('-', size)).Append(" ");
                                    values.Append("| ").Append(field.Value.PadLeft(size)).Append(" ");
                                }

                                if (writeToFileOption.HasValue())
                                {
                                    var writeToFilename = "results.md";

                                    if (!File.Exists(writeToFilename))
                                    {
                                        File.CreateText(writeToFilename).Dispose();
                                    }

                                    if (!File.ReadLines(writeToFilename).Any())
                                    {
                                        File.AppendAllText(writeToFilename, header + "|" + Environment.NewLine);
                                        File.AppendAllText(writeToFilename, separator + "|" + Environment.NewLine);
                                    }

                                    File.AppendAllText(writeToFilename, values + "|" + Environment.NewLine);
                                }


                                // Render all results if --quiet not set
                                if (iterations > 1 && !_quiet)
                                {
                                    QuietLog("All results:");

                                    QuietLog(header + "|");
                                    QuietLog(separator + "|");

                                    foreach (var result in results)
                                    {
                                        var tmpDescription = result.Description;
                                        result.Description = samples.Contains(result) ? "✓" : "✗";
                                        var localFields = BuildFields(result);

                                        var localValues = new StringBuilder();
                                        foreach (var field in localFields)
                                        {
                                            var size = Math.Max(field.Key.Length, field.Value.Length);
                                            localValues.Append("| ").Append(field.Value.PadLeft(size)).Append(" ");
                                        }

                                        result.Description = tmpDescription;
                                        QuietLog(localValues + "|");
                                    }

                                    QuietLog("");
                                }

                                if (diffOption.HasValue())
                                {
                                    var diffFilename = diffOption.Value();

                                    // If the filename has no extensions, add a default one
                                    if (!Path.HasExtension(diffFilename))
                                    {
                                        diffFilename += ".bench.json";
                                    }

                                    if (!File.Exists(diffFilename))
                                    {
                                        QuietLog($"Could not find the specified file '{diffFilename}'");
                                        return -1;
                                    }
                                    else
                                    {
                                        var compareTo = JsonConvert.DeserializeObject<Statistics>(File.ReadAllText(diffFilename));

                                        var compareToFields = BuildFields(compareTo);
                                        var compareToBuilder = new StringBuilder();

                                        header.Clear();
                                        separator.Clear();
                                        values.Clear();

                                        compareToFields.Add(new KeyValuePair<string, string>("Ratio", $"{1:f2}"));
                                        fields.Add(new KeyValuePair<string, string>("Ratio", $"{(average.RequestsPerSecond / compareTo.RequestsPerSecond):f2}"));

                                        // Headers
                                        for (var f = 0; f < fields.Count; f++)
                                        {
                                            var field = fields[f];
                                            var comparedToField = compareToFields[f];

                                            var size = Math.Max(Math.Max(field.Key.Length, field.Value.Length), comparedToField.Value.Length);
                                            header.Append("| ").Append(field.Key.PadLeft(size)).Append(" ");
                                            separator.Append("| ").Append(new String('-', size)).Append(" ");
                                            compareToBuilder.Append("| ").Append(comparedToField.Value.PadLeft(size)).Append(" ");
                                            values.Append("| ").Append(field.Value.PadLeft(size)).Append(" ");
                                        }

                                        QuietLog(header + "|");
                                        QuietLog(separator + "|");
                                        QuietLog(compareToBuilder + "|");
                                        QuietLog(values + "|");
                                    }
                                }
                                else if (markdownOption.HasValue())
                                {
                                    QuietLog(header + "|");
                                    QuietLog(separator + "|");
                                    QuietLog(values + "|");
                                }
                                else
                                {
                                    QuietLog($"RequestsPerSecond:           {average.RequestsPerSecond:n0}");
                                    QuietLog($"Max CPU (%):                 {average.Cpu}");
                                    QuietLog($"WorkingSet (MB):             {average.WorkingSet:n0}");
                                    QuietLog($"Avg. Latency (ms):           {average.LatencyOnLoad}");
                                    QuietLog($"Startup (ms):                {average.StartupMain}");
                                    QuietLog($"First Request (ms):          {average.FirstRequest}");
                                    QuietLog($"Latency (ms):                {average.Latency}");
                                    QuietLog($"Total Requests:              {average.TotalRequests:n0}");
                                    QuietLog($"Duration: (ms)               {average.Duration:n0}");
                                    QuietLog($"Socket Errors:               {average.SocketErrors:n0}");
                                    QuietLog($"Bad Responses:               {average.BadResponses:n0}");
                                    QuietLog($"SDK:                         {serverJob.SdkVersion}");
                                    QuietLog($"Runtime:                     {serverJob.RuntimeVersion}");
                                    QuietLog($"ASP.NET Core:                {serverJob.AspNetCoreVersion}");

                                    if (average.Other.Any())
                                    {
                                        QuietLog("");
                                        QuietLog("Counters:");

                                        foreach (var counter in Counters)
                                        {
                                            if (!average.Other.ContainsKey(counter.Name))
                                            {
                                                continue;
                                            }

                                            QuietLog($"{(counter.DisplayName + ":").PadRight(29, ' ')}{average.Other[counter.Name].ToString(counter.Format)}");
                                        }
                                    }
                                }

                                if (saveOption.HasValue())
                                {
                                    var saveFilename = saveOption.Value();

                                    // If the filename has no extensions, add a default one
                                    if (!Path.HasExtension(saveFilename))
                                    {
                                        saveFilename += ".bench.json";
                                    }

                                    // Existing files are overriden
                                    if (File.Exists(saveFilename))
                                    {
                                        File.Delete(saveFilename);
                                    }

                                    File.WriteAllText(saveFilename, JsonConvert.SerializeObject(average));

                                    Log($"Results saved in '{saveFilename}'");
                                }
                            }

                            if (i == iterations && serializer != null && !String.IsNullOrEmpty(sqlConnectionString))
                            {
                                sqlTask = sqlTask.ContinueWith(async t =>
                                {
                                    Log("Writing results to SQL...");
                                    try
                                    {
                                        await serializer.WriteJobResultsToSqlAsync(
                                            serverJob: serverJob,
                                            clientJob: clientJobs[0],
                                            connectionString: sqlConnectionString,
                                            tableName: _tableName,
                                            path: serverJob.Path,
                                            session: session,
                                            description: description,
                                            statistics: average,
                                            longRunning: span > TimeSpan.Zero);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log("Error writing results to SQL: " + ex);
                                        return;
                                    }

                                    Log("Finished writing results to SQL.");
                                });
                            }
                        }

                        spanLoop = spanLoop + 1;
                    } while (DateTime.UtcNow - startTime < span);

                    if (!sqlTask.IsCompleted)
                    {
                        Log("Job finished, waiting for SQL to complete.");
                        await sqlTask;
                    }

                    Log($"Stopping scenario '{scenario}' on benchmark server...");

                    response = await _httpClient.PostAsync(serverJobUri + "/stop", new StringContent(""));
                    LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");
                    var jobStoppedUtc = DateTime.UtcNow;

                    // Wait for Stop state
                    do
                    {
                        await Task.Delay(1000);

                        LogVerbose($"GET {serverJobUri}...");
                        response = await _httpClient.GetAsync(serverJobUri);
                        responseContent = await response.Content.ReadAsStringAsync();

                        LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

                        if (response.StatusCode == HttpStatusCode.NotFound || String.IsNullOrEmpty(responseContent))
                        {
                            Log($"The job was forcibly stopped by the server.");
                            return 0;
                        }

                        serverJob = JsonConvert.DeserializeObject<ServerJob>(responseContent);

                        if (DateTime.UtcNow - jobStoppedUtc > TimeSpan.FromSeconds(30))
                        {
                            // The job needs to be deleted
                            Log($"Server didn't stop the job in the expected time, deleting it ...");

                            break;
                        }

                    } while (serverJob.State != ServerState.Stopped);

                    if (_displayOutput)
                    {

                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            // Convert LF
                            serverJob.Output = serverJob.Output.Replace("\n", Environment.NewLine);
                        }

                        Log(serverJob.Output, notime: true);
                    }

                    // Download netperf file
                    if (_enableEventPipeOption.HasValue())
                    {
                        var uri = serverJobUri + "/eventpipe";
                        LogVerbose("GET " + uri);

                        try
                        {
                            var traceOutputFileName = traceDestination;
                            if (traceOutputFileName == null || !traceOutputFileName.EndsWith(".netperf", StringComparison.OrdinalIgnoreCase))
                            {
                                traceOutputFileName = traceOutputFileName + "." + DateTime.Now.ToString("MM-dd-HH-mm-ss") + "." + rpsStr + ".netperf";
                            }

                            Log($"Downloading trace: {traceOutputFileName}");
                            await _httpClient.DownloadFileAsync(uri, serverJobUri, traceOutputFileName);
                        }
                        catch (Exception e)
                        {
                            Log($"Error while downloading EventPipe file {EventPipeOutputFile}");
                            LogVerbose(e.Message);
                        }
                    }

                }
                catch (Exception e)
                {
                    Log($"Interrupting due to an unexpected exception");
                    Log(e.ToString());

                    return -1;
                }
                finally
                {
                    if (serverJobUri != null)
                    {
                        // Download published application
                        if (fetch)
                        {
                            try
                            {
                                Log($"Downloading published application...");
                                if (fetchDestination == null || !fetchDestination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    // If it does not end with a *.zip then we add a DATE.zip to it
                                    if (String.IsNullOrEmpty(fetchDestination))
                                    {
                                        fetchDestination = "published";
                                    }

                                    fetchDestination = fetchDestination + "." + DateTime.Now.ToString("MM-dd-HH-mm-ss") + ".zip";
                                }

                                var uri = serverJobUri + "/fetch";
                                Log($"Creating published archive: {fetchDestination}");
                                await File.WriteAllBytesAsync(fetchDestination, await _httpClient.GetByteArrayAsync(uri));
                            }
                            catch (Exception e)
                            {
                                Log($"Error while downloading published application");
                                LogVerbose(e.Message);
                            }
                        }

                        // Download files
                        if (downloadFiles != null && downloadFiles.Any())
                        {
                            foreach (var file in downloadFiles)
                            {
                                Log($"Downloading file {file}");
                                var uri = serverJobUri + "/download?path=" + HttpUtility.UrlEncode(file);
                                LogVerbose("GET " + uri);

                                try
                                {
                                    var filename = file;
                                    var counter = 1;
                                    while (File.Exists(filename))
                                    {
                                        filename = Path.GetFileNameWithoutExtension(file) + counter++ + Path.GetExtension(file);
                                    }

                                    await _httpClient.DownloadFileAsync(uri, serverJobUri, filename);
                                }
                                catch (Exception e)
                                {
                                    Log($"Error while downloading file {file}, skipping ...");
                                    LogVerbose(e.Message);
                                    continue;
                                }
                            }
                        }

                        Log($"Deleting scenario '{scenario}' on benchmark server...");

                        LogVerbose($"DELETE {serverJobUri}...");
                        response = await _httpClient.DeleteAsync(serverJobUri);
                        LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");

                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            Log($@"Server job was not found, it must have been aborted. Possible cause:
                            - Issue while cloning the repository (GitHub unresponsive)
                            - Issue while restoring (MyGet/NuGet unresponsive)
                            - Issue while building
                            - Issue while running (Timeout)"
                            );
                        }

                        response.EnsureSuccessStatusCode();
                    }
                }
            }

            return 0;
        }

        private static List<KeyValuePair<string, string>> BuildFields(Statistics average)
            => new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Description", average.Description),
                new KeyValuePair<string, string>("RPS", $"{average.RequestsPerSecond:n0}"),
                new KeyValuePair<string, string>("CPU (%)", $"{average.Cpu}"),
                new KeyValuePair<string, string>("Memory (MB)", $"{average.WorkingSet:n0}"),
                new KeyValuePair<string, string>("Avg. Latency (ms)", $"{average.LatencyOnLoad}"),
                new KeyValuePair<string, string>("Startup (ms)", $"{average.StartupMain}"),
                new KeyValuePair<string, string>("First Request (ms)", $"{average.FirstRequest}"),
                new KeyValuePair<string, string>("Latency (ms)", $"{average.Latency}"),
                new KeyValuePair<string, string>("Errors", $"{average.SocketErrors + average.BadResponses}"),
            };

        private static async Task<int> UploadFileAsync(string filename, ServerJob serverJob, string uri)
        {
            Log($"Uploading {filename} to {uri}");

            try
            {
                var outputFileSegments = filename.Split(';');
                var uploadFilename = outputFileSegments[0];

                if (!File.Exists(uploadFilename))
                {
                    Console.WriteLine($"File '{uploadFilename}' could not be loaded.");
                    return 8;
                }

                var destinationFilename = outputFileSegments.Length > 1
                    ? outputFileSegments[1]
                    : Path.GetFileName(uploadFilename);

                using (var requestContent = new MultipartFormDataContent())
                {
                    var fileContent = uploadFilename.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? new StreamContent(await _httpClient.GetStreamAsync(uploadFilename))
                        : new StreamContent(new FileStream(uploadFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.Asynchronous | FileOptions.SequentialScan));

                    using (fileContent)
                    {
                        requestContent.Add(fileContent, nameof(AttachmentViewModel.Content), Path.GetFileName(uploadFilename));
                        requestContent.Add(new StringContent(serverJob.Id.ToString()), nameof(AttachmentViewModel.Id));
                        requestContent.Add(new StringContent(destinationFilename), nameof(AttachmentViewModel.DestinationFilename));

                        await _httpClient.PostAsync(uri, requestContent);
                    }
                }
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"An error occured while uploading a file.", e);
            }

            return 0;
        }

        private static async Task<int> UploadScriptAsync(string filename, ClientJob clientJob, Uri clientJobUri)
        {
            try
            {
                var requestContent = new MultipartFormDataContent();

                var fileContent = filename.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new StreamContent(await _httpClient.GetStreamAsync(filename))
                    : new StreamContent(new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.Asynchronous | FileOptions.SequentialScan));

                requestContent.Add(new StringContent(clientJob.Id.ToString()), nameof(ScriptViewModel.Id));
                requestContent.Add(fileContent, nameof(ScriptViewModel.Content), Path.GetFileName(filename));
                requestContent.Add(new StringContent(Path.GetFileName(filename)), nameof(ScriptViewModel.SourceFileName));

                Log($"Sending {Path.GetFileName(filename)}");

                var result = await _httpClient.PostAsync(clientJobUri + "/script", requestContent);
                result.EnsureSuccessStatusCode();
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"An error occured while uploading a file.", e);
            }

            return 0;
        }

        private static async Task<ClientJob> RunClientJob(string scenarioName, Uri clientUri, Uri serverJobUri, string serverBenchmarkUri, CommandOption scriptFileOption)
        {
            var clientJob = new ClientJob(_clientJob) { ServerBenchmarkUri = serverBenchmarkUri };
            var benchmarkUri = new Uri(serverBenchmarkUri);
            clientJob.Headers["Host"] = benchmarkUri.Host + ":" + benchmarkUri.Port;
            Uri clientJobUri = null;
            try
            {
                Log($"Starting scenario {scenarioName} on benchmark client...");

                var clientJobsUri = new Uri(clientUri, "/jobs");
                var clientContent = JsonConvert.SerializeObject(clientJob);

                LogVerbose($"POST {clientJobsUri} {clientContent}...");
                var response = await _httpClient.PostAsync(clientJobsUri, new StringContent(clientContent, Encoding.UTF8, "application/json"));
                var responseContent = await response.Content.ReadAsStringAsync();
                LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");
                response.EnsureSuccessStatusCode();

                clientJobUri = new Uri(clientUri, response.Headers.Location);

                LogVerbose($"GET {clientJobUri}...");
                response = await _httpClient.GetAsync(clientJobUri);
                responseContent = await response.Content.ReadAsStringAsync();
                clientJob = JsonConvert.DeserializeObject<ClientJob>(responseContent);

                var allWrkScripts = new List<string>();

                if (scriptFileOption.HasValue())
                {
                    allWrkScripts.AddRange(scriptFileOption.Values);
                }

                if ((clientJob.Client == Worker.Wrk || clientJob.Client == Worker.Wrk2) && clientJob.ClientProperties.ContainsKey("Scripts"))
                {
                    allWrkScripts.AddRange(clientJob.ClientProperties["Scripts"].Split(';'));
                }

                foreach (var scriptFile in allWrkScripts)
                {
                    var result = await UploadScriptAsync(scriptFile, clientJob, clientJobUri);

                    if (result != 0)
                    {
                        Log($"Error while sending custom script to client. interrupting");
                        return null;
                    }
                }

                response = await _httpClient.PostAsync(clientJobUri + "/start", new StringContent(""));
                responseContent = await response.Content.ReadAsStringAsync();
                LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");
                response.EnsureSuccessStatusCode();

                Log($"Client Job ready: {clientJobUri}");

                while (true)
                {
                    // Retry block, prevent any network communication error from stopping the job
                    await RetryOnExceptionAsync(5, async () =>
                    {
                        // Ping server job to keep it alive
                        LogVerbose($"GET {serverJobUri}/touch...");
                        response = await _httpClient.GetAsync(serverJobUri + "/touch");

                        LogVerbose($"GET {clientJobUri}...");
                        response = await _httpClient.GetAsync(clientJobUri);
                        responseContent = await response.Content.ReadAsStringAsync();

                        LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");
                    }, 1000);

                    if (!response.IsSuccessStatusCode)
                    {
                        responseContent = await response.Content.ReadAsStringAsync();
                        Log(responseContent);
                        Log($"Job halted by the client");
                        break;
                    }

                    clientJob = JsonConvert.DeserializeObject<ClientJob>(responseContent);

                    if (clientJob.State == ClientState.Running || clientJob.State == ClientState.Completed)
                    {
                        break;
                    }
                    else
                    {
                        await Task.Delay(1000);
                    }
                }

                while (true)
                {
                    // Retry block, prevent any network communication error from stopping the job
                    await RetryOnExceptionAsync(5, async () =>
                    {
                        // Ping server job to keep it alive
                        LogVerbose($"GET {serverJobUri}/touch...");
                        response = await _httpClient.GetAsync(serverJobUri + "/touch");

                        LogVerbose($"GET {clientJobUri}...");
                        response = await _httpClient.GetAsync(clientJobUri);
                        responseContent = await response.Content.ReadAsStringAsync();
                        LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");
                    }, 1000);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        Log($"Job forcibly stopped on the client, halting driver");
                        break;
                    }

                    clientJob = JsonConvert.DeserializeObject<ClientJob>(responseContent);

                    if (clientJob.State == ClientState.Completed)
                    {
                        Log($"Scenario {scenarioName} completed on benchmark client");
                        LogVerbose($"Output: {clientJob.Output}");

                        if (!String.IsNullOrWhiteSpace(clientJob.Error))
                        {
                            Log($"Error: {clientJob.Error}");
                        }

                        break;
                    }
                    else
                    {
                        await Task.Delay(1000);
                    }
                }
            }
            finally
            {
                if (clientJobUri != null)
                {
                    HttpResponseMessage response = null;

                    await RetryOnExceptionAsync(5, async () =>
                    {
                        Log($"Deleting scenario {scenarioName} on benchmark client...");

                        LogVerbose($"DELETE {clientJobUri}...");
                        response = await _httpClient.DeleteAsync(clientJobUri);
                        LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");
                    }, 1000);

                    if (response.StatusCode != HttpStatusCode.NotFound)
                    {
                        response.EnsureSuccessStatusCode();
                    }
                }
            }

            return clientJob;
        }

        private static string _filecache = null;

        private static async Task<string> DownloadTemporaryFileAsync(string uri, Uri serverJobUri)
        {
            if (_filecache == null)
            {
                _filecache = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            }

            Directory.CreateDirectory(_filecache);

            _temporaryFolders.Add(_filecache);

            var filehashname = Path.Combine(_filecache, uri.GetHashCode().ToString());

            if (!File.Exists(filehashname))
            {
                await _httpClient.DownloadFileAsync(uri, serverJobUri, filehashname);
            }

            return filehashname;
        }

        private static void CleanTemporaryFiles()
        {
            foreach (var temporaryFolder in _temporaryFolders)
            {
                if (temporaryFolder != null && Directory.Exists(temporaryFolder))
                {
                    Directory.Delete(temporaryFolder, true);
                }
            }
        }

        private static async Task InvokeApplicationEndpoint(Uri serverJobUri, string path)
        {
            var uri = serverJobUri + "/invoke?path=" + HttpUtility.UrlEncode(path);
            Console.WriteLine(await _httpClient.GetStringAsync(uri));
        }

        private static void QuietLog(string message)
        {
            Console.WriteLine(message);
        }

        internal static void Log(string message, bool notime = false, bool error = false)
        {
            if (error)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }

            if (!_quiet)
            {
                var time = DateTime.Now.ToString("hh:mm:ss.fff");
                if (notime)
                {
                    Console.WriteLine(message);
                }
                else
                {
                    Console.WriteLine($"[{time}] {message}");
                }
            }

            Console.ResetColor();
        }

        internal static void LogVerbose(string message)
        {
            if (_verbose && !_quiet)
            {
                Log(message);
            }
        }

        private async static Task RetryOnExceptionAsync(int retries, Func<Task> operation, int milliSecondsDelay = 0)
        {
            var attempts = 0;
            do
            {
                try
                {
                    attempts++;
                    await operation();
                    return;
                }
                catch (Exception e)
                {
                    if (attempts == retries + 1)
                    {
                        throw;
                    }

                    Log($"Attempt {attempts} failed: {e.Message}");

                    if (milliSecondsDelay > 0)
                    {
                        await Task.Delay(milliSecondsDelay);
                    }
                }
            } while (true);
        }

        private static void DoCreateFromDirectory(string sourceDirectoryName, string destinationArchiveFileName)
        {
            sourceDirectoryName = Path.GetFullPath(sourceDirectoryName);

            // We ensure the name ends with '\' or '/'
            if (!sourceDirectoryName.EndsWith(Path.AltDirectorySeparatorChar))
            {
                sourceDirectoryName += Path.AltDirectorySeparatorChar;
            }

            destinationArchiveFileName = Path.GetFullPath(destinationArchiveFileName);

            DirectoryInfo di = new DirectoryInfo(sourceDirectoryName);

            using (ZipArchive archive = ZipFile.Open(destinationArchiveFileName, ZipArchiveMode.Create))
            {
                var basePath = di.FullName;

                var ignoreFile = IgnoreFile.Parse(Path.Combine(sourceDirectoryName, ".gitignore"));

                foreach (var gitFile in ignoreFile.ListDirectory(sourceDirectoryName))
                {
                    var localPath = gitFile.Path.Substring(sourceDirectoryName.Length);
                    LogVerbose($"Adding {localPath}");
                    var entry = archive.CreateEntryFromFile(gitFile.Path, localPath);
                }
            }
        }

        private static Dictionary<string, string> ExpandTraceArguments(string arguments)
        {
            var segments = arguments.Split(';');

            var result = new Dictionary<string, string>(segments.Length);

            foreach (var segment in segments)
            {
                var values = segment.Split('=', 2);

                var key = values[0].Trim();

                // GCCollectOnly
                if (values.Length == 1)
                {
                    result[key] = "";
                }
                else
                {
                    var value = values[1].Trim();

                    if (String.IsNullOrWhiteSpace(value))
                    {
                        result[key] = null;
                    }
                    else
                    {
                        result[key] = value;
                    }
                }
            }

            return result;
        }

        // ANSI Console mode support
        private static bool IsConsoleApp => _clientJob.Client == Worker.None || _clientJob.Client == Worker.BenchmarkDotNet;

        private const int STD_OUTPUT_HANDLE = -11;
        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
        private const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

        [DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        public static extern uint GetLastError();

        private static Func<IEnumerable<double>, double> Percentile(int percentile)
        {
            return list =>
            {
                var orderedList = list.OrderBy(x => x).ToArray();

                var nth = (int)Math.Ceiling((double)orderedList.Length * percentile / 100);

                return orderedList[nth];
            };
        }
    }
}
