// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Benchmarks.ClientJob;
using Benchmarks.ServerJob;
using BenchmarksDriver.Serializers;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BenchmarksDriver
{
    public class Program
    {
        private static TimeSpan _timeout = TimeSpan.FromMinutes(5);

        private static readonly HttpClient _httpClient;
        private static readonly HttpClientHandler _httpClientHandler;

        private static ClientJob _clientJob;
        private static string _tableName = "AspNetBenchmarks";
        private const string EventPipeOutputFile = "eventpipe.netperf";
        private static string EventPipeConfig = "Microsoft-DotNETCore-SampleProfiler:FFFF:5,Microsoft-Windows-DotNETRuntime:4c14fccbd:5";

        // Default to arguments which should be sufficient for collecting trace of default Plaintext run
        private const string _defaultTraceArguments = "BufferSizeMB=1024;CircularMB=1024;clrEvents=JITSymbols;kernelEvents=process+thread+ImageLoad+Profile";

        private static CommandOption
            _outputArchiveOption,
            _buildArchiveOption,
            _buildFileOption,
            _outputFileOption,
            _serverSourceOption,
            _clientSourceOption,
            _serverProjectOption,
            _clientProjectOption,
            _initializeOption,
            _cleanOption,
            _memoryLimitOption,
            _enableEventPipeOption,
            _eventPipeArgumentsOption,
            _initSubmodulesOption,
            _branchOption,
            _hashOption,
            _noGlobalJsonOption,
            _serverCollectCountersOption,
            _clientCollectCountersOption,
            _noStartupLatencyOption,
            _displayBuildOption,
            _displayClientOutputOption,
            _displayServerOutputOption,
            _serverCollectStartupOption,
            _clientCollectStartupOption,
            _serverSdkOption,
            _clientSdkOption,
            _serverRuntimeVersionOption,
            _clientRuntimeVersionOption,
            _serverSelfContainedOption,
            _clientSelfContainedOption,
            _serverAspnetCoreVersionOption,
            _clientAspnetCoreVersionOption
            ;

        private static Dictionary<string, string> _deprecatedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "--projectfile", "--project-file" },
            { "--outputfile", "--output-file" },
            { "--clientName", "--client-name" }
        };

        private static Dictionary<string, string> _synonymArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "--aspnet", "--aspnetcoreversion" },
            { "--runtime", "--runtimeversion" },
            { "--clientThreads", "--client-threads" },
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
                    Log.Write($"WARNING: '{arg}' has been deprecated, in the future please use '{mappedArg}'.");
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
            var clientNameOption = app.Option("--client-name",
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
            _displayClientOutputOption = app.Option("--client-display-output",
                "Displays the standard output from the client job.", CommandOptionType.NoValue);
            _displayServerOutputOption = app.Option("--server-display-output",
                "Displays the standard output from the server job.", CommandOptionType.NoValue);
            _displayBuildOption = app.Option("--display-build",
                "Displays the standard output from the build step.", CommandOptionType.NoValue);
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
            var serverScenarioOption = app.Option("--server-scenario",
                "Server scenario to run", CommandOptionType.SingleValue);
            var clientScenarioOption = app.Option("--client-scenario",
                "Client scenario to run", CommandOptionType.SingleValue);
            var schemeOption = app.Option("-m|--scheme",
                "Scheme (http, https, h2, h2c). Default is http.", CommandOptionType.SingleValue);
            var webHostOption = app.Option(
                "-w|--webHost",
                "WebHost (e.g., KestrelLibuv, KestrelSockets, HttpSys). Default is KestrelSockets.",
                CommandOptionType.SingleValue);
            _serverAspnetCoreVersionOption = app.Option("--server-aspnet-version",
                "ASP.NET Core packages version on the server (Current, Latest, or custom value). Current is the latest public version (2.0.*), Latest is the currently developed one. Default is Latest (2.2-*).", CommandOptionType.SingleValue);
            _clientAspnetCoreVersionOption = app.Option("--client-aspnet-version",
                "ASP.NET Core packages version on the server (Current, Latest, or custom value). Current is the latest public version (2.0.*), Latest is the currently developed one. Default is Latest (2.2-*).", CommandOptionType.SingleValue);
            _serverRuntimeVersionOption = app.Option("--server-runtime-version",
                ".NET Core Runtime version on the server (Current, Latest, Edge or custom value). Current is the latest public version, Latest is the one enlisted, Edge is the latest available. Default is Latest (2.2.0-*).", CommandOptionType.SingleValue);
            _clientRuntimeVersionOption = app.Option("--client-runtime-version",
                ".NET Core Runtime version on the client (Current, Latest, Edge or custom value). Current is the latest public version, Latest is the one enlisted, Edge is the latest available. Default is Latest (2.2.0-*).", CommandOptionType.SingleValue);
            var serverArgumentsOption = app.Option("--server-args",
                "Argument to pass to the server application. (e.g., --server-args \"--raw=true\" --server-args \"single_value\")", CommandOptionType.MultipleValue);
            var clientArgumentsOption = app.Option("--client-args",
                "Argument to pass to the client application. The server url can be injected using {{server-url}}. (e.g., --client-args \"--raw=true\" --client-args \"single_value\")", CommandOptionType.MultipleValue);
            var serverNoArgumentsOptions = app.Option("--server-no-arguments",
                "Removes any predefined arguments from the server application command line.", CommandOptionType.NoValue);
            var clientNoArgumentsOptions = app.Option("--client-no-arguments",
                "Removes any predefined arguments from the client application command line.", CommandOptionType.NoValue);
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
            _serverSourceOption = app.Option("--server-source",
                "Local folder containing the project for the server.", CommandOptionType.SingleValue);
            _clientSourceOption = app.Option("--client-source",
                "Local folder containing the project for the client.", CommandOptionType.SingleValue);
            var dockerFileOption = app.Option("-df|--docker-file",
                "File path of the Docker script. (e.g, \"frameworks/CSharp/aspnetcore/aspcore.dockerfile\")", CommandOptionType.SingleValue);
            var dockerContextOption = app.Option("-dc|--docker-context",
                "Docker context directory. Defaults to the Docker file directory. (e.g., \"frameworks/CSharp/aspnetcore/\")", CommandOptionType.SingleValue);
            var dockerImageOption = app.Option("-di|--docker-image",
                "The name of the Docker image to create. If not net one will be created from the Docker file name. (e.g., \"aspnetcore21\")", CommandOptionType.SingleValue);
            _serverProjectOption = app.Option("--server-project-file",
                "Relative path of the server project. (e.g., \"src/Benchmarks/Benchmarks.csproj)\"", CommandOptionType.SingleValue);
            _clientProjectOption = app.Option("--client-project-file",
                "Relative path of the client project. (e.g., \"src/Benchmarks/Benchmarks.csproj)\"", CommandOptionType.SingleValue);
            _initSubmodulesOption = app.Option("--init-submodules",
                "When set will init submodules on the repository.", CommandOptionType.NoValue);
            var useRuntimeStoreOption = app.Option("--runtime-store",
                "Runs the benchmarks using the runtime store (2.0) or shared aspnet framework (2.1).", CommandOptionType.NoValue);
            _serverSelfContainedOption = app.Option("--server-self-contained",
                "Publishes the server application as standalone.", CommandOptionType.NoValue);
            _clientSelfContainedOption = app.Option("--client-self-contained",
                "Publishes the client application as standalone.", CommandOptionType.NoValue);
            _outputFileOption = app.Option("--output-file",
                "Output file attachment. Format is 'path[;destination]'. Path can be a URL. e.g., " +
                "\"--output-file c:\\build\\Microsoft.AspNetCore.Mvc.dll\", " +
                "\"--output-file c:\\files\\samples\\picture.png;wwwroot\\picture.png\"",
                CommandOptionType.MultipleValue);
            _buildFileOption = app.Option("--build-file",
                "Build file attachment. Format is 'path[;destination]'. Path can be a URL. e.g., " +
                "\"--build-file c:\\build\\Microsoft.AspNetCore.Mvc.dll\", " +
                "\"--build-file c:\\files\\samples\\picture.png;wwwroot\\picture.png\"",
                CommandOptionType.MultipleValue);
            _outputArchiveOption = app.Option("--output-archive",
                "Output archive attachment. Format is 'path[;destination]'. Path can be a URL. e.g., " +
                "\"--output-archive c:\\build\\Microsoft.AspNetCore.Mvc.zip\", " +
                "\"--output-archive http://raw/github.com/pictures.zip;wwwroot\\pictures\"",
                CommandOptionType.MultipleValue);
            _buildArchiveOption = app.Option("--build-archive",
                "Build archive attachment. Format is 'path[;destination]'. Path can be a URL. e.g., " +
                "\"--build-archive c:\\build\\Microsoft.AspNetCore.Mvc.zip\", " +
                "\"--build-archive http://raw/github.com/pictures.zip;wwwroot\\pictures\"",
                CommandOptionType.MultipleValue);
            var scriptFileOption = app.Option("--script",
                "WRK script path. File path can be a URL. e.g., " +
                "\"--script c:\\scripts\\post.lua\"",
                CommandOptionType.MultipleValue);
            var collectTraceOption = app.Option("--collect-trace",
                "Collect a PerfView trace.", CommandOptionType.NoValue);
            _serverCollectStartupOption = app.Option("--server-trace-startup",
                "Includes the startup phase in the server trace.", CommandOptionType.NoValue);
            _clientCollectStartupOption = app.Option("--client-trace-startup",
                "Includes the startup phase in the client trace.", CommandOptionType.NoValue);
            _serverCollectCountersOption = app.Option("--server-collect-counters",
                "Collect event counters on the server.", CommandOptionType.NoValue);
            _clientCollectCountersOption = app.Option("--client-collect-counters",
                "Collect event counters on the client.", CommandOptionType.NoValue);
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
            _serverSdkOption = app.Option("--server-sdk-version",
                "SDK version to use on the server", CommandOptionType.SingleValue);
            _clientSdkOption = app.Option("--client-sdk-version",
                "SDK version to use on the client", CommandOptionType.SingleValue);
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
            _noStartupLatencyOption = app.Option("-nsl|--no-startup-latency",
                "Skip startup latency measurement.", CommandOptionType.NoValue);

            var serverJobOption = app.Option("--server-jobs",
                "The path or url to the server jobs definition.", CommandOptionType.SingleValue);

            var clientJobOption = app.Option("--client-jobs",
                "The path or url to the server jobs definition.", CommandOptionType.SingleValue);

            app.OnExecute(() =>
            {

                Log.IsQuiet = quietOption.HasValue();
                Log.IsVerbose = verboseOption.HasValue();

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
                var serverJobDefinitionPathOrUrl = serverJobOption.Value();
                var cientJobDefinitionPathOrUrl = clientJobOption.Value();
                var iterations = 1;
                var exclude = 0;

                var sqlConnectionString = sqlConnectionStringOption.Value();
                var span = TimeSpan.Zero;

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

                foreach (var client in clients)
                {
                    try
                    {
                        using (var cts = new CancellationTokenSource(2000))
                        {
                            var response = _httpClient.GetAsync(client, cts.Token).Result;
                            response.EnsureSuccessStatusCode();
                        }
                    }
                    catch
                    {
                        Console.WriteLine($"The specified client url '{client}' is invalid or not responsive.");
                        return 2;
                    }
                }

                try
                {
                    using (var cts = new CancellationTokenSource(2000))
                    {
                        var response = _httpClient.GetAsync(server, cts.Token).Result;
                        response.EnsureSuccessStatusCode();
                    }
                }
                catch
                {
                    Console.WriteLine($"The specified server url '{server}' is invalid or not responsive.");
                }

                if (sqlTableOption.HasValue())
                {
                    _tableName = sqlTableOption.Value();
                }

                // Load the server job from the definition path and the scenario name
                var serverJob = BuildServerJob(serverJobDefinitionPathOrUrl, serverScenarioOption.Value() ?? "Default", _serverProjectOption);

                // Load the server job from the definition path and the scenario name
                ServerJob clientJob = null;

                // If a client job definition is defined, build it
                if (!String.IsNullOrWhiteSpace(cientJobDefinitionPathOrUrl))
                {
                    clientJob = BuildServerJob(cientJobDefinitionPathOrUrl, clientScenarioOption.Value() ?? "Default", _clientProjectOption);
                }

                //var jobOptions = mergedServerJob.ToObject<JobOptions>();

                //if (pathOption.HasValue() && jobOptions.Paths != null && jobOptions.Paths.Count > 0)
                //{
                //    jobOptions.Paths.Add(serverJob.Path);

                //    if (!jobOptions.Paths.Any(p => string.Equals(p, serverJob.Path, StringComparison.OrdinalIgnoreCase)) &&
                //        !jobOptions.Paths.Any(p => string.Equals(p, "/" + serverJob.Path, StringComparison.OrdinalIgnoreCase)))
                //    {
                //        Console.WriteLine($"Scenario '{serverScenarioName}' does not support {pathOption.LongName} '{pathOption.Value()}'. Choose from:");
                //        Console.WriteLine($"'{string.Join("', '", jobOptions.Paths)}'");
                //        return 6;
                //    }
                //}

                // If the KnownHeaders property of the job definition is a string, fetch it from the Headers enum
                // We only look at it no Preset is defined on the commandline
                //if (!String.IsNullOrEmpty(jobOptions.PresetHeaders) && !headersOption.HasValue())
                //{
                //    if (!Enum.TryParse(jobOptions.PresetHeaders, ignoreCase: true, result: out headers))
                //    {
                //        Console.WriteLine($"Unknown KnownHeaders value: '{jobOptions.PresetHeaders}'. Choose from: None, Html, Json, Plaintext.");
                //    }
                //}

                // Scenario can't be set in job definitions
                serverJob.WebHost = webHost;

                foreach (var argument in serverJob.OutputFilesArgument)
                {
                    _outputFileOption.Values.Add(argument);
                }

                foreach (var argument in serverJob.OutputArchivesArgument)
                {
                    _outputArchiveOption.Values.Add(argument);
                }

                foreach (var argument in serverJob.BuildFilesArgument)
                {
                    _buildFileOption.Values.Add(argument);
                }

                foreach (var argument in serverJob.BuildArchivesArgument)
                {
                    _buildArchiveOption.Values.Add(argument);
                }

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
                if (_serverSelfContainedOption.HasValue())
                {
                    serverJob.SelfContained = true;
                }
                else
                {
                    if (_outputFileOption.HasValue() || _outputArchiveOption.HasValue())
                    {
                        serverJob.SelfContained = true;

                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Log.Write("WARNING: '--self-contained' has been set implicitly as custom local files are used.");
                        Console.ResetColor();
                    }
                    else if (_serverAspnetCoreVersionOption.HasValue() || _serverRuntimeVersionOption.HasValue())
                    {
                        serverJob.SelfContained = true;

                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Log.Write("WARNING: '--self-contained' has been set implicitly as custom runtime versions are used.");
                        Console.ResetColor();
                    }

                }
                if (kestrelThreadCountOption.HasValue())
                {
                    serverJob.KestrelThreadCount = int.Parse(kestrelThreadCountOption.Value());
                }
                if (serverNoArgumentsOptions.HasValue())
                {
                    serverJob.NoArguments = true;
                }
                if (serverArgumentsOption.HasValue())
                {
                    serverJob.Arguments = serverJob.Arguments ?? "";

                    foreach (var arg in serverArgumentsOption.Values)
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
                if (_serverAspnetCoreVersionOption.HasValue())
                {
                    serverJob.AspNetCoreVersion = _serverAspnetCoreVersionOption.Value();
                }
                if (_serverRuntimeVersionOption.HasValue())
                {
                    serverJob.RuntimeVersion = _serverRuntimeVersionOption.Value();
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
                if (_serverProjectOption.HasValue())
                {
                    serverJob.Source.Project = _serverProjectOption.Value();
                }
                if (noCleanOption.HasValue())
                {
                    serverJob.NoClean = true;
                }
                if (frameworkOption.HasValue())
                {
                    serverJob.Framework = frameworkOption.Value();
                }
                if (_serverSdkOption.HasValue())
                {
                    serverJob.SdkVersion = _serverSdkOption.Value();
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
                if (_serverCollectStartupOption.HasValue())
                {
                    serverJob.CollectStartup = true;
                }
                if (_serverCollectCountersOption.HasValue())
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
                if (_outputFileOption.HasValue())
                {
                    foreach (var outputFile in _outputFileOption.Values)
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

                if (clientArgumentsOption.HasValue())
                {
                    clientJob.Arguments = clientJob.Arguments ?? "";

                    foreach (var arg in clientArgumentsOption.Values)
                    {
                        var equalSignIndex = arg.IndexOf('=');

                        if (equalSignIndex == -1)
                        {
                            clientJob.Arguments += " " + arg;
                        }
                        else
                        {
                            clientJob.Arguments += $" {arg.Substring(0, equalSignIndex)} {arg.Substring(equalSignIndex + 1)}";
                        }
                    }
                }

                if (clientNoArgumentsOptions.HasValue())
                {
                    clientJob.NoArguments = true;
                }

                if (_clientCollectCountersOption.HasValue())
                {
                    clientJob.CollectCounters = true;
                }

                if (_clientCollectStartupOption.HasValue())
                {
                    clientJob.CollectStartup = true;
                }

                if (_clientSdkOption.HasValue())
                {
                    clientJob.SdkVersion = _clientSdkOption.Value();
                }

                if (_clientSelfContainedOption.HasValue())
                {
                    clientJob.SelfContained = true;
                }
                else
                {
                    if (_outputFileOption.HasValue() || _outputArchiveOption.HasValue())
                    {
                        clientJob.SelfContained = true;

                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Log.Write("WARNING: '--self-contained' has been set implicitly as custom local files are used.");
                        Console.ResetColor();
                    }
                    else if (_clientAspnetCoreVersionOption.HasValue() || _clientRuntimeVersionOption.HasValue())
                    {
                        clientJob.SelfContained = true;

                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Log.Write("WARNING: '--self-contained' has been set implicitly as custom runtime versions are used.");
                        Console.ResetColor();
                    }

                }

                if (_clientAspnetCoreVersionOption.HasValue())
                {
                    clientJob.AspNetCoreVersion = _clientAspnetCoreVersionOption.Value();
                }

                if (_clientRuntimeVersionOption.HasValue())
                {
                    clientJob.RuntimeVersion = _clientRuntimeVersionOption.Value();
                }

                //var mergedClientJob = new JObject(defaultJob);
                //mergedClientJob.Merge(job);
                //_clientJob = mergedClientJob.ToObject<ClientJob>();

                //if (clientNameOption.HasValue())
                //{
                //    if (!Enum.TryParse<Worker>(clientNameOption.Value(), ignoreCase: true, result: out var worker))
                //    {
                //        Log.Write($"Could not find worker {clientNameOption.Value()}");
                //        return 9;
                //    }

                //    _clientJob.Client = worker;
                //}

                //if (benchmarkdotnetOption.HasValue())
                //{
                //    if (String.IsNullOrEmpty(serverJob.Scenario))
                //    {
                //        serverJob.Scenario = "Benchmark.NET";
                //    }

                //    serverJob.NoArguments = true;
                //    _clientJob.Client = Worker.BenchmarkDotNet;

                //    var bdnScenario = benchmarkdotnetOption.Value();
                //    if (String.IsNullOrEmpty(bdnScenario))
                //    {
                //        bdnScenario = "*";
                //    }

                //    serverJob.Arguments += $" --inProcess --cli {{{{benchmarks-cli}}}} --filter {bdnScenario}";
                //}

                //if (consoleOption.HasValue())
                //{
                //    serverJob.IsConsoleApp = true;
                //    _clientJob.Client = Worker.None;
                //    serverJob.CollectStartup = true;
                //}

                //Log.Write($"Using worker {_clientJob.Client}");

                //if (_clientJob.Client == Worker.BenchmarkDotNet)
                //{
                //    serverJob.IsConsoleApp = true;
                //    serverJob.ReadyStateText = "BenchmarkRunner: Start";
                //    serverJob.CollectStartup = true;
                //}

                //// The ready state option overrides BenchmarDotNet's value
                //if (readyTextOption.HasValue())
                //{
                //    serverJob.ReadyStateText = readyTextOption.Value();
                //}

                //// Override default ClientJob settings if options are set
                //if (connectionsOption.HasValue())
                //{
                //    _clientJob.Connections = int.Parse(connectionsOption.Value());
                //}
                //if (clientThreadsOption.HasValue())
                //{
                //    _clientJob.Threads = int.Parse(clientThreadsOption.Value());
                //}
                //if (durationOption.HasValue())
                //{
                //    _clientJob.Duration = int.Parse(durationOption.Value());
                //}
                //if (warmupOption.HasValue())
                //{
                //    _clientJob.Warmup = int.Parse(warmupOption.Value());
                //}
                //if (noWarmupOption.HasValue())
                //{
                //    _clientJob.Warmup = 0;
                //}
                //if (clientProperties.HasValue())
                //{
                //    foreach (var property in clientProperties.Values)
                //    {
                //        var index = property.IndexOf('=');

                //        if (index == -1)
                //        {
                //            Console.WriteLine($"Invalid property variable, '=' not found: '{property}'");
                //            return 9;
                //        }
                //        else
                //        {
                //            _clientJob.ClientProperties[property.Substring(0, index)] = property.Substring(index + 1);
                //        }
                //    }
                //}

                //_clientJob.ClientProperties["protocol"] = schemeValue;

                //if (methodOption.HasValue())
                //{
                //    _clientJob.Method = methodOption.Value();
                //}
                //if (querystringOption.HasValue())
                //{
                //    _clientJob.Query = querystringOption.Value();
                //}
                //if (span > TimeSpan.Zero)
                //{
                //    _clientJob.SpanId = Guid.NewGuid().ToString("n");
                //}

                //switch (headers)
                //{
                //    case Headers.None:
                //        break;

                //    case Headers.Html:
                //        _clientJob.Headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
                //        _clientJob.Headers["Connection"] = "keep-alive";
                //        break;

                //    case Headers.Json:
                //        _clientJob.Headers["Accept"] = "application/json,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7";
                //        _clientJob.Headers["Connection"] = "keep-alive";
                //        break;

                //    case Headers.Plaintext:
                //        _clientJob.Headers["Accept"] = "text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7";
                //        _clientJob.Headers["Connection"] = "keep-alive";
                //        break;
                //}

                //if (headerOption.HasValue())
                //{
                //    foreach (var header in headerOption.Values)
                //    {
                //        var segments = header.Split('=', 2);

                //        if (segments.Length != 2)
                //        {
                //            Console.WriteLine($"Invalid http header, '=' not found: '{header}'");
                //            return 9;
                //        }

                //        _clientJob.Headers[segments[0].Trim()] = segments[1].Trim();
                //    }
                //}

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
                    clientJob,
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
                    scriptFileOption,
                    markdownOption,
                    writeToFileOption,
                    requiredOperatingSystem,
                    saveOption,
                    diffOption
                    ).Result;
            });

            // Resolve response files from urls

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
                // TODO: clean the files for all jobs
                //CleanTemporaryFiles();
            }
        }

        private static async Task<int> Run(
            Uri serverUri,
            Uri[] clientUris,
            string sqlConnectionString,
            ServerJob serverJob,
            ServerJob clientJob,
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
            CommandOption scriptFileOption,
            CommandOption markdownOption,
            CommandOption writeToFileOption,
            Benchmarks.ServerJob.OperatingSystem? requiredOperatingSystem,
            CommandOption saveOption,
            CommandOption diffOption
            )
        {
            var scenario = serverJob.Scenario;
            
            string serverJobUri = null;
            HttpResponseMessage response = null;

            var results = new List<Statistics>();
            ClientJob[] clientJobs = null;
            Job jobOnServer = null;

            IResultsSerializer serializer = null;
            //var serializer = WorkerFactory.CreateResultSerializer(_clientJob);

            //if (serializer != null && !string.IsNullOrWhiteSpace(sqlConnectionString))
            //{
            //    await serializer.InitializeDatabaseAsync(sqlConnectionString, _tableName);
            //}

            serverJob.DriverVersion = 2;
            clientJob.DriverVersion = 2;

            Log.Write($"Running session '{session}' with description '{description}'");

            for (var i = 1; i <= iterations; i++)
            {
                if (iterations > 1)
                {
                    Log.Write($"Job {i} of {iterations}");
                }

                try
                {
                    jobOnServer = new Job(serverJob, serverUri);

                    // Start server
                    serverJobUri = await jobOnServer.StartAsync(
                        requiredOperatingSystem?.ToString(),
                        IsConsoleApp,
                        _serverSourceOption,
                        _outputArchiveOption,
                        _buildArchiveOption,
                        _outputFileOption,
                        _buildFileOption
                        );

                    var serverBenchmarkUri = serverJob.Url;

                    await Task.Delay(200);  // Make it clear on traces when startup has finished and warmup begins.

                    jobOnServer.StartKeepAlive();

                    TimeSpan latencyNoLoad = TimeSpan.Zero, latencyFirstRequest = TimeSpan.Zero;

                    // Reset this before each iteration
                    //_clientJob.SkipStartupLatencies = _noStartupLatencyOption.HasValue();


                    // TODO: WARMUP

                    //if (!IsConsoleApp && _clientJob.Warmup != 0)
                    //{
                    //    Log.Write("Warmup");
                    //    var duration = _clientJob.Duration;

                    //    _clientJob.Duration = _clientJob.Warmup;

                    //    if (clientUris.Any())
                    //    {
                    //        // Warmup using the first client
                    //        clientJobs = new[] { await RunClientJob(scenario, clientUris[0], serverJobUri, serverBenchmarkUri, scriptFileOption) };
                    //    }

                    //    // Store the latency as measured on the warmup job
                    //    // The first client is used to measure the latencies
                    //    latencyNoLoad = clientJobs[0].LatencyNoLoad;
                    //    latencyFirstRequest = clientJobs[0].LatencyFirstRequest;

                    //    _clientJob.Duration = duration;
                    //    await Task.Delay(200);  // Make it clear on traces when warmup stops and measuring begins.
                    //}

                    // Prevent the actual run from updating the startup statistics
                    //_clientJob.SkipStartupLatencies = true;

                    var startTime = DateTime.UtcNow;
                    var spanLoop = 0;
                    var sqlTask = Task.CompletedTask;
                    string rpsStr = "";

                    do
                    {
                        if (span > TimeSpan.Zero)
                        {
                            Log.Write($"Starting client job iteration {spanLoop}. Running since {startTime.ToLocalTime()}, with {((startTime + span) - DateTime.UtcNow):c} remaining.");

                            // Clear the measures from the server job and update it on the server
                            if (spanLoop > 0)
                            {
                                results.Clear();
                                response = await _httpClient.PostAsync(serverJobUri + "/resetstats", new StringContent(""));
                                response.EnsureSuccessStatusCode();
                            }
                        }

                        // Set the URL on which the server should be reached from the clients as an environment variable
                        clientJob.EnvironmentVariables.Add("SERVER_URL", jobOnServer.ServerJob.Url);

                        // Look for {{server-url}} placeholder in the client arguments
                        clientJob.Arguments = clientJob.Arguments?.Replace("{{server-url}}", jobOnServer.ServerJob.Url);

                        var jobsOnClient = clientUris.Select(clientUri => new Job(clientJob, clientUri)).ToArray();
                        
                        // Don't run the client job for None and BenchmarkDotNet
                        if (!IsConsoleApp)
                        {
                            //var tasks = clientUris.Select(clientUri => RunClientJob(scenario, clientUri, serverJobUri, serverBenchmarkUri, scriptFileOption)).ToArray();
                            //await Task.WhenAll(tasks);
                            //clientJobs = tasks.Select(x => x.Result).ToArray();


                            // Wait for all clients to start
                            await Task.WhenAll(
                                jobsOnClient.Select(jobOnClient =>
                                {
                                    // Start server
                                    return jobOnClient.StartAsync(
                                        requiredOperatingSystem?.ToString(),
                                        true, // assume console app for now as we don't have a way to set it for the client
                                        _clientSourceOption,
                                        _outputArchiveOption,
                                        _buildArchiveOption,
                                        _outputFileOption,
                                        _buildFileOption
                                    );
                                })
                            );

                            foreach (var jobOnClient in jobsOnClient)
                            {
                                jobOnClient.StartKeepAlive();
                            }

                            // Wait for all clients to stop
                            while( !jobsOnClient.All(client => client.ServerJob.State == ServerState.Stopped))
                            {
                                foreach (var jobOnClient in jobsOnClient)
                                {
                                    await jobOnClient.TryUpdateStateAsync();
                                }

                                await Task.Delay(1000);
                            }

                        }
                        else
                        {
                            // Don't wait for the client job as we are not starting it
                            clientJobs = new[] { new ClientJob { State = ClientState.Completed } };

                            // Wait until the server has stopped
                            var now = DateTime.UtcNow;

                            while (jobOnServer.ServerJob.State != ServerState.Stopped && (DateTime.UtcNow - now < _timeout))
                            {
                                await jobOnServer.TryUpdateStateAsync();

                                await Task.Delay(1000);
                            }

                            if (jobOnServer.ServerJob.State == ServerState.Stopped)
                            {
                                // Try to extract BenchmarkDotNet statistics
                                //if (_clientJob.Client == Worker.BenchmarkDotNet)
                                //{
                                //    //await BenchmarkDotNetUtils.DownloadResultFiles(serverJobUri, _httpClient, (BenchmarkDotNetSerializer)serializer);
                                //}
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.White;
                                Log.Write($"Server job running for more than {_timeout}, stopping...");
                                Console.ResetColor();
                                jobOnServer.ServerJob.State = ServerState.Failed;
                            }
                        }

                        if (jobsOnClient.Any(client => client.ServerJob.State == ServerState.Failed))
                        {
                            // Stop all client jobs
                            await Task.WhenAll(jobsOnClient.Select(client => client.StopAsync()));
                            await Task.WhenAll(jobsOnClient.Select(client => client.DeleteAsync()));
                        }
                        else if (jobsOnClient.All(client => client.ServerJob.State == ServerState.Stopped) && jobOnServer.ServerJob.State != ServerState.Failed)
                        {
                            // Stop all client jobs
                            await Task.WhenAll(jobsOnClient.Select(client => client.StopAsync()));
                            await Task.WhenAll(jobsOnClient.Select(client => client.DeleteAsync()));

                            Log.Verbose($"Client Jobs completed");

                            if (span == TimeSpan.Zero && i == iterations && !String.IsNullOrEmpty(shutdownEndpoint))
                            {
                                Log.Write($"Invoking '{shutdownEndpoint}' on benchmarked application...");
                                await InvokeApplicationEndpoint(serverJobUri, shutdownEndpoint);
                            }

                            // Load latest state of server job
                            await jobOnServer.TryUpdateStateAsync();

                            // Download R2R log
                            if (collectR2RLog)
                            {
                                downloadFiles.Add("r2r." + serverJob.ProcessId);
                            }

                            //if (clientJobs[0].Warmup == 0)
                            //{
                            //    latencyNoLoad = clientJobs[0].LatencyNoLoad;
                            //    latencyFirstRequest = clientJobs[0].LatencyFirstRequest;
                            //}

                            // Display Environment information

                            Log.Quiet("");
                            Log.Quiet($"Server ");
                            Log.Quiet($"-------");
                            Log.Quiet("");
                            Log.Quiet($"SDK:                         {jobOnServer.ServerJob.SdkVersion}");
                            Log.Quiet($"Runtime:                     {jobOnServer.ServerJob.RuntimeVersion}");
                            Log.Quiet($"ASP.NET Core:                {jobOnServer.ServerJob.AspNetCoreVersion}");

                            WriteMeasures(jobOnServer);

                            if (_displayServerOutputOption.HasValue())
                            {
                                Log.DisplayOutput(jobOnServer.ServerJob.Output);
                            }

                            Log.Quiet("");
                            Log.Quiet($"Clients");
                            Log.Quiet($"-------------------");
                            Log.Quiet("");
                            Log.Quiet($"SDK:                         {jobsOnClient.First().ServerJob.SdkVersion}");
                            Log.Quiet($"Runtime:                     {jobsOnClient.First().ServerJob.RuntimeVersion}");
                            Log.Quiet($"ASP.NET Core:                {jobsOnClient.First().ServerJob.AspNetCoreVersion}");

                            foreach (var jobOnClient in jobsOnClient)
                            {
                                WriteMeasures(jobOnClient);

                                if (_displayClientOutputOption.HasValue())
                                {
                                    Log.DisplayOutput(jobOnClient.ServerJob.Output);
                                }
                            }

                            var statistics = new Statistics
                            {
                                //RequestsPerSecond = clientJobs.Sum(clientJob => clientJob.RequestsPerSecond),
                                //LatencyOnLoad = clientJobs.Average(clientJob => clientJob.Latency.Average),
                                //Cpu = cpu,
                                //WorkingSet = workingSet,
                                //StartupMain = serverJob.StartupMainMethod.TotalMilliseconds,
                                //BuildTime = serverJob.BuildTime.TotalMilliseconds,
                                //PublishedSize = serverJob.PublishedSize,
                                //FirstRequest = latencyFirstRequest.TotalMilliseconds,
                                //Latency = latencyNoLoad.TotalMilliseconds,
                                //SocketErrors = clientJobs.Sum(clientJob => clientJob.SocketErrors),
                                //BadResponses = clientJobs.Sum(clientJob => clientJob.BadResponses),

                                //LatencyAverage = clientJobs.Average(clientJob => clientJob.Latency.Average),
                                //Latency50Percentile = clientJobs.Average(clientJob => clientJob.Latency.Within50thPercentile),
                                //Latency75Percentile = clientJobs.Average(clientJob => clientJob.Latency.Within75thPercentile),
                                //Latency90Percentile = clientJobs.Average(clientJob => clientJob.Latency.Within90thPercentile),
                                //Latency99Percentile = clientJobs.Average(clientJob => clientJob.Latency.Within99thPercentile),
                                //MaxLatency = clientJobs.Average(clientJob => clientJob.Latency.MaxLatency),
                                //TotalRequests = clientJobs.Sum(clientJob => clientJob.Requests),
                                //Duration = clientJobs[0].ActualDuration.TotalMilliseconds
                            };

                            results.Add(statistics);

                            if (iterations > 1 && !IsConsoleApp)
                            {
                                Log.Verbose($"RequestsPerSecond:           {statistics.RequestsPerSecond}");
                                Log.Verbose($"Max CPU (%):                 {statistics.Cpu}");
                                Log.Verbose($"WorkingSet (MB):             {statistics.WorkingSet}");
                                Log.Verbose($"Latency (ms):                {statistics.Latency}");
                                Log.Verbose($"Socket Errors:               {statistics.SocketErrors}");
                                Log.Verbose($"Bad Responses:               {statistics.BadResponses}");

                                // Don't display these startup numbers on stress load
                                if (spanLoop == 0)
                                {
                                    Log.Verbose($"Latency on load (ms):        {statistics.LatencyOnLoad}");
                                    Log.Verbose($"Startup Main (ms):           {statistics.StartupMain}");
                                    Log.Verbose($"First Request (ms):          {statistics.FirstRequest}");
                                    Log.Verbose($"Build Time (ms):             {statistics.BuildTime}");
                                    Log.Verbose($"Published Size (KB):         {statistics.PublishedSize}");
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
                                Log.Write($"EventPipe config: {EventPipeConfig}");
                            }

                            // Collect Trace
                            if (jobOnServer.ServerJob.Collect)
                            {
                                Log.Write($"Post-processing profiler trace, this can take 10s of seconds...");

                                Log.Write($"Trace arguments: {jobOnServer.ServerJob.CollectArguments}");

                                var uri = serverJobUri + "/trace";
                                response = await _httpClient.PostAsync(uri, new StringContent(""));
                                response.EnsureSuccessStatusCode();

                                while (true)
                                {
                                    
                                    if (!await jobOnServer.TryUpdateStateAsync())
                                    {
                                        Log.Write($"The job was forcibly stopped by the server.");
                                        return 1;
                                    }

                                    if (jobOnServer.ServerJob.State == ServerState.TraceCollected)
                                    {
                                        break;
                                    }
                                    else if (jobOnServer.ServerJob.State == ServerState.TraceCollecting)
                                    {
                                        // Server is collecting the trace
                                    }
                                    else
                                    {
                                        Log.Write($"Unexpected state: {jobOnServer.ServerJob.State}");
                                    }

                                    await Task.Delay(1000);
                                }

                                var traceExtension = jobOnServer.ServerJob.OperatingSystem == Benchmarks.ServerJob.OperatingSystem.Windows
                                    ? ".etl.zip"
                                    : ".trace.zip";

                                var traceOutputFileName = traceDestination;
                                if (traceOutputFileName == null || !traceOutputFileName.EndsWith(traceExtension, StringComparison.OrdinalIgnoreCase))
                                {
                                    traceOutputFileName = traceOutputFileName + "." + DateTime.Now.ToString("MM-dd-HH-mm-ss") + "." + rpsStr + traceExtension;
                                }

                                try
                                {
                                    Log.Write($"Downloading trace: {traceOutputFileName}");
                                    await _httpClient.DownloadFileAsync(uri, serverJobUri, traceOutputFileName);
                                }
                                catch (HttpRequestException)
                                {
                                    Log.Write($"FAILED: The trace was not successful");
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
                                    BuildTime = Math.Round(samples.Average(x => x.BuildTime)),
                                    PublishedSize = Math.Round(samples.Average(x => x.PublishedSize)),
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

                                //if (serializer != null)
                                //{
                                //    serializer.ComputeAverages(average, samples);
                                //}

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
                                if (iterations > 1 && !Log.IsQuiet)
                                {
                                    Log.Quiet("All results:");

                                    Log.Quiet(header + "|");
                                    Log.Quiet(separator + "|");

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
                                        Log.Quiet(localValues + "|");
                                    }

                                    Log.Quiet("");
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
                                        Log.Quiet($"Could not find the specified file '{diffFilename}'");
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

                                        Log.Quiet(header + "|");
                                        Log.Quiet(separator + "|");
                                        Log.Quiet(compareToBuilder + "|");
                                        Log.Quiet(values + "|");
                                    }
                                }
                                else if (markdownOption.HasValue())
                                {
                                    Log.Quiet(header + "|");
                                    Log.Quiet(separator + "|");
                                    Log.Quiet(values + "|");
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

                                    Log.Write($"Results saved in '{saveFilename}'");
                                }
                            }

                            if (i == iterations && serializer != null && !String.IsNullOrEmpty(sqlConnectionString))
                            {
                                sqlTask = sqlTask.ContinueWith(async t =>
                                {
                                    Log.Write("Writing results to SQL...");
                                    try
                                    {
                                        await serializer.WriteJobResultsToSqlAsync(
                                            serverJob: serverJob,
                                            clientJob: clientJobs[0],
                                            connectionString: sqlConnectionString,
                                            tableName: _tableName,
                                            path: jobOnServer.ServerJob.Path,
                                            session: session,
                                            description: description,
                                            statistics: average,
                                            longRunning: span > TimeSpan.Zero);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Write("Error writing results to SQL: " + ex);
                                        return;
                                    }

                                    Log.Write("Finished writing results to SQL.");
                                });
                            }
                        }

                        spanLoop = spanLoop + 1;
                    } while (DateTime.UtcNow - startTime < span);

                    if (!sqlTask.IsCompleted)
                    {
                        Log.Write("Job finished, waiting for SQL to complete.");
                        await sqlTask;
                    }

                    await jobOnServer.StopAsync();

                    // Download netperf file
                    if (_enableEventPipeOption.HasValue())
                    {
                        var uri = serverJobUri + "/eventpipe";
                        Log.Verbose("GET " + uri);

                        try
                        {
                            var traceOutputFileName = traceDestination;
                            if (traceOutputFileName == null || !traceOutputFileName.EndsWith(".netperf", StringComparison.OrdinalIgnoreCase))
                            {
                                traceOutputFileName = traceOutputFileName + "." + DateTime.Now.ToString("MM-dd-HH-mm-ss") + "." + rpsStr + ".netperf";
                            }

                            Log.Write($"Downloading trace: {traceOutputFileName}");
                            await _httpClient.DownloadFileAsync(uri, serverJobUri, traceOutputFileName);
                        }
                        catch (Exception e)
                        {
                            Log.Write($"Error while downloading EventPipe file {EventPipeOutputFile}");
                            Log.Verbose(e.Message);
                        }
                    }

                }
                catch (Exception e)
                {
                    Log.Write($"Interrupting due to an unexpected exception");
                    Log.Write(e.ToString());

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
                                Log.Write($"Downloading published application...");
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
                                Log.Write($"Creating published archive: {fetchDestination}");
                                await File.WriteAllBytesAsync(fetchDestination, await _httpClient.GetByteArrayAsync(uri));
                            }
                            catch (Exception e)
                            {
                                Log.Write($"Error while downloading published application");
                                Log.Verbose(e.Message);
                            }
                        }

                        // Download files
                        if (downloadFiles != null && downloadFiles.Any())
                        {
                            foreach (var file in downloadFiles)
                            {
                                Log.Write($"Downloading file {file}");
                                var uri = serverJobUri + "/download?path=" + HttpUtility.UrlEncode(file);
                                Log.Verbose("GET " + uri);

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
                                    Log.Write($"Error while downloading file {file}, skipping ...");
                                    Log.Verbose(e.Message);
                                    continue;
                                }
                            }
                        }

                        // Display build log
                        if (_displayBuildOption.HasValue())
                        {
                            try
                            {
                                Log.Write($"Downloading build log...");

                                var uri = serverJobUri + "/buildlog";

                                Log.DisplayOutput(await _httpClient.GetStringAsync(uri));
                            }
                            catch (Exception e)
                            {
                                Log.Write($"Error while downloading build logs");
                                Log.Verbose(e.Message);
                            }
                        }

                        await jobOnServer.DeleteAsync();
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
                new KeyValuePair<string, string>("Build Time (ms)", $"{average.BuildTime}"),
                new KeyValuePair<string, string>("Published Size (KB)", $"{average.PublishedSize}"),
                new KeyValuePair<string, string>("First Request (ms)", $"{average.FirstRequest}"),
                new KeyValuePair<string, string>("Latency (ms)", $"{average.Latency}"),
                new KeyValuePair<string, string>("Errors", $"{average.SocketErrors + average.BadResponses}"),
            };

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

                Log.Write($"Sending {Path.GetFileName(filename)}");

                var result = await _httpClient.PostAsync(clientJobUri + "/script", requestContent);
                result.EnsureSuccessStatusCode();
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"An error occured while uploading a file.", e);
            }

            return 0;
        }

        private static async Task<ClientJob> RunClientJob(string scenarioName, Uri clientUri, string serverJobUri, string serverBenchmarkUri, CommandOption scriptFileOption)
        {
            var clientJob = new ClientJob(_clientJob) { ServerBenchmarkUri = serverBenchmarkUri };
            var benchmarkUri = new Uri(serverBenchmarkUri);
            clientJob.Headers["Host"] = benchmarkUri.Host + ":" + benchmarkUri.Port;
            Uri clientJobUri = null;
            try
            {
                Log.Write($"Starting scenario {scenarioName} on benchmark client...");

                var clientJobsUri = new Uri(clientUri, "/jobs");
                var clientContent = JsonConvert.SerializeObject(clientJob);

                Log.Verbose($"POST {clientJobsUri} {clientContent}...");
                var response = await _httpClient.PostAsync(clientJobsUri, new StringContent(clientContent, Encoding.UTF8, "application/json"));
                var responseContent = await response.Content.ReadAsStringAsync();
                Log.Verbose($"{(int)response.StatusCode} {response.StatusCode}");
                response.EnsureSuccessStatusCode();

                clientJobUri = new Uri(clientUri, response.Headers.Location);

                Log.Verbose($"GET {clientJobUri}...");
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
                        Log.Write($"Error while sending custom script to client. interrupting");
                        return null;
                    }
                }

                response = await _httpClient.PostAsync(clientJobUri + "/start", new StringContent(""));
                responseContent = await response.Content.ReadAsStringAsync();
                Log.Verbose($"{(int)response.StatusCode} {response.StatusCode}");
                response.EnsureSuccessStatusCode();

                Log.Write($"Client Job ready: {clientJobUri}");

                while (true)
                {
                    // Retry block, prevent any network communication error from stopping the job
                    await RetryOnExceptionAsync(5, async () =>
                    {
                        // Ping server job to keep it alive
                        Log.Verbose($"GET {serverJobUri}/touch...");
                        response = await _httpClient.GetAsync(serverJobUri + "/touch");

                        Log.Verbose($"GET {clientJobUri}...");
                        response = await _httpClient.GetAsync(clientJobUri);
                        responseContent = await response.Content.ReadAsStringAsync();

                        Log.Verbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");
                    }, 1000);

                    if (!response.IsSuccessStatusCode)
                    {
                        responseContent = await response.Content.ReadAsStringAsync();
                        Log.Write(responseContent);
                        Log.Write($"Job halted by the client");
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
                        Log.Verbose($"GET {serverJobUri}/touch...");
                        response = await _httpClient.GetAsync(serverJobUri + "/touch");

                        Log.Verbose($"GET {clientJobUri}...");
                        response = await _httpClient.GetAsync(clientJobUri);
                        responseContent = await response.Content.ReadAsStringAsync();
                        Log.Verbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");
                    }, 1000);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        Log.Write($"Job forcibly stopped on the client, halting driver");
                        break;
                    }

                    clientJob = JsonConvert.DeserializeObject<ClientJob>(responseContent);

                    if (clientJob.State == ClientState.Completed)
                    {
                        Log.Write($"Scenario {scenarioName} completed on benchmark client");
                        Log.Verbose($"Output: {clientJob.Output}");

                        if (!String.IsNullOrWhiteSpace(clientJob.Error))
                        {
                            Log.Write($"Error: {clientJob.Error}");
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
                        Log.Write($"Deleting scenario {scenarioName} on benchmark client...");

                        Log.Verbose($"DELETE {clientJobUri}...");
                        response = await _httpClient.DeleteAsync(clientJobUri);
                        Log.Verbose($"{(int)response.StatusCode} {response.StatusCode}");
                    }, 1000);

                    if (response.StatusCode != HttpStatusCode.NotFound)
                    {
                        response.EnsureSuccessStatusCode();
                    }
                }
            }

            return clientJob;
        }

        private static async Task InvokeApplicationEndpoint(string serverJobUri, string path)
        {
            var uri = serverJobUri + "/invoke?path=" + HttpUtility.UrlEncode(path);
            Console.WriteLine(await _httpClient.GetStringAsync(uri));
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

                    Log.Write($"Attempt {attempts} failed: {e.Message}");

                    if (milliSecondsDelay > 0)
                    {
                        await Task.Delay(milliSecondsDelay);
                    }
                }
            } while (true);
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

        /// <summary>
        /// Builds a ServerJob object from a jobs definition file and a scenario name
        /// </summary>
        /// <param name="scenarioName"></param>
        /// <param name="jobDefinitionPathOrUrl"></param>
        /// <returns></returns>
        public static ServerJob BuildServerJob(string jobDefinitionPathOrUrl, string scenarioName, CommandOption projectOption)
        {
            JobDefinition serverJobDefinitions;

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
                    throw new Exception($"Job definition '{jobDefinitionPathOrUrl}' could not be loaded.");
                }

                serverJobDefinitions = JsonConvert.DeserializeObject<JobDefinition>(jobDefinitionContent);

                if (!serverJobDefinitions.ContainsKey(scenarioName))
                {
                    if (scenarioName == "Default")
                    {
                        throw new Exception($"Default job not found in the job definition file.");
                    }
                    else
                    {
                        throw new Exception($"Job named '{scenarioName}' not found in the job definition file.");
                    }
                }
                else
                {
                    // Normalizes the scenario name by using the one from the job definition
                    scenarioName = serverJobDefinitions.First(x => String.Equals(x.Key, scenarioName, StringComparison.OrdinalIgnoreCase)).Key;
                }
            }
            else
            {
                // For now the --server-job is mandatory

                // TODO: implement
                //
                //if ((!(repositoryOption.HasValue() || sourceOption.HasValue()) ||
                //    !projectOption.HasValue()) &&
                //    !dockerFileOption.HasValue())
                //{
                //    Console.WriteLine($"Repository or source folder and project are mandatory when no job definition is specified.");
                //    return 9;
                //}

                serverJobDefinitions = new JobDefinition();
                serverJobDefinitions.Add(scenarioName, new JObject());
            }

            var mergeOptions = new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge };

            if (!serverJobDefinitions.TryGetValue("Default", out var defaultJob))
            {
                defaultJob = new JObject();
            }

            var job = serverJobDefinitions[scenarioName];

            // Building ServerJob

            var mergedServerJob = new JObject(defaultJob);
            mergedServerJob.Merge(job);

            var serverJob = mergedServerJob.ToObject<ServerJob>();

            serverJob.Scenario = scenarioName;

            if (projectOption.HasValue())
            {
                serverJob.Source.Project = projectOption.Value();
            }

            return serverJob;
        }

        // ANSI Console mode support
        private static bool IsConsoleApp => false; // _clientJob.Client == Worker.None || _clientJob.Client == Worker.BenchmarkDotNet;


        private static Func<IEnumerable<double>, double> Percentile(int percentile)
        {
            return list =>
            {
                var orderedList = list.OrderBy(x => x).ToArray();

                var nth = (int)Math.Ceiling((double)orderedList.Length * percentile / 100);

                return orderedList[nth];
            };
        }

        private static void WriteMeasures(Job job)
        {
            // Handle old server versions that don't expose measurements
            if (!job.ServerJob.Measurements.Any() || !job.ServerJob.Metadata.Any())
            {
                return;
            }

            // Group by name for easy lookup
            var measurements = job.ServerJob.Measurements.GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.ToList());
            var maxWidth = job.ServerJob.Metadata.Max(x => x.ShortDescription.Length) + 2;

            var previousSource = "";

            foreach (var metadata in job.ServerJob.Metadata)
            {
                if (!measurements.ContainsKey(metadata.Name))
                {
                    continue;
                }

                if (previousSource != metadata.Source)
                {
                    Log.Quiet("");
                    Log.Quiet($"## {metadata.Source}:");

                    previousSource = metadata.Source;
                }

                double result = 0;

                switch (metadata.Aggregate)
                {
                    case Operation.Avg:
                        result = measurements[metadata.Name].Average(x => Convert.ToDouble(x.Value));
                        break;

                    case Operation.Count:
                        result = measurements[metadata.Name].Count();
                        break;

                    case Operation.Max:
                        result = measurements[metadata.Name].Max(x => Convert.ToDouble(x.Value));
                        break;

                    case Operation.Median:
                        result = Percentile(50)(measurements[metadata.Name].Select(x => Convert.ToDouble(x.Value)));
                        break;

                    case Operation.Min:
                        result = measurements[metadata.Name].Min(x => Convert.ToDouble(x.Value));
                        break;

                    case Operation.Sum:
                        result = measurements[metadata.Name].Sum(x => Convert.ToDouble(x.Value));
                        break;
                }

                Console.WriteLine($"{(metadata.ShortDescription + ":").PadRight(maxWidth)} {result.ToString(metadata.Format)}");

            }
        }
    }
}
