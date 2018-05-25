// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
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
        private static bool _verbose;

        private static readonly HttpClient _httpClient = new HttpClient();

        private static ClientJob _clientJob;
        private static string _tableName = "AspNetBenchmarks";

        // Default to arguments which should be sufficient for collecting trace of default Plaintext run
        private const string _defaultTraceArguments = "BufferSizeMB=1024;CircularMB=1024";

        public static int Main(string[] args)
        {
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
                "URL of benchmark client", CommandOptionType.SingleValue);
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
            var sessionOption = app.Option("--session",
                "A logical identifier to group related jobs.", CommandOptionType.SingleValue);
            var descriptionOption = app.Option("--description",
                "The description of the job.", CommandOptionType.SingleValue);
            var iterationsOption = app.Option("-i|--iterations",
                "The number of iterations.", CommandOptionType.SingleValue);
            var excludeOption = app.Option("-x|--exclude",
                "The number of best and worst and jobs to skip.", CommandOptionType.SingleValue);
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

            // ServerJob Options
            var databaseOption = app.Option("--database",
                "The type of database to run the benchmarks with (PostgreSql, SqlServer or MySql). Default is PostgreSql.", CommandOptionType.SingleValue);
            var connectionFilterOption = app.Option("-cf|--connectionFilter",
                "Assembly-qualified name of the ConnectionFilter", CommandOptionType.SingleValue);
            var kestrelThreadCountOption = app.Option("--kestrelThreadCount",
                "Maps to KestrelServerOptions.ThreadCount.",
                CommandOptionType.SingleValue);
            var scenarioOption = app.Option("-n|--scenario",
                "Benchmark scenario to run", CommandOptionType.SingleValue);
            var schemeOption = app.Option("-m|--scheme",
                "Scheme (http or https).  Default is http.", CommandOptionType.SingleValue);
            var webHostOption = app.Option(
                "-w|--webHost",
                "WebHost (e.g., KestrelLibuv, KestrelSockets, HttpSys). Default is KestrelSockets.",
                CommandOptionType.SingleValue);
            var aspnetCoreVersionOption = app.Option("--aspnetCoreVersion",
                "ASP.NET Core packages version (Current, Latest, or custom value). Current is the latest public version (2.0.*), Latest is the currently developped one. Default is Latest (2.1-*).", CommandOptionType.SingleValue);
            var runtimeVersionOption = app.Option("--runtimeVersion",
                ".NET Core Runtime version (Current, Latest, Edge or custom value). Current is the latest public version, Latest is the one enlisted, Edge is the latest available. Default is Latest (2.1.0-*).", CommandOptionType.SingleValue);
            var argumentsOption = app.Option("--arguments",
                "Arguments to pass to the application. (e.g., \"--raw true\")", CommandOptionType.SingleValue);
            var portOption = app.Option("--port",
                "The port used to request the benchmarked application. Default is 5000.", CommandOptionType.SingleValue);
            var repositoryOption = app.Option("-r|--repository",
                "Git repository containing the project to test.", CommandOptionType.SingleValue);
            var projectOption = app.Option("--projectFile",
                "Relative path of the project to test in the repository. (e.g., \"src/Benchmarks/Benchmarks.csproj)\"", CommandOptionType.SingleValue);
            var useRuntimeStoreOption = app.Option("--runtime-store",
                "Runs the benchmarks using the runtime store (2.0) or shared aspnet framework (2.1).", CommandOptionType.NoValue);
            var selfContainedOption = app.Option("--self-contained",
                "Publishes the .NET Core runtime with the application.", CommandOptionType.NoValue);
            var outputFileOption = app.Option("--outputFile",
                "Output file attachment. Format is 'path[;destination]'. FilePath can be a URL. e.g., " +
                "\"--outputFile c:\\build\\Microsoft.AspNetCore.Mvc.dll\", " +
                "\"--outputFile c:\\files\\samples\\picture.png;wwwroot\\picture.png\"",
                CommandOptionType.MultipleValue);
            var runtimeFileOption = app.Option("--runtimeFile",
                "Runtime file attachment. Format is 'path[;destination]', e.g., " +
                "\"--runtimeFile c:\\build\\System.Net.Security.dll\"",
                CommandOptionType.MultipleValue);
            var collectTraceOption = app.Option("--collect-trace",
                "Collect a PerfView trace.", CommandOptionType.NoValue);
            var traceArgumentsOption = app.Option("--trace-arguments",
                $"Arguments used when collecting a PerfView trace.  Defaults to \"{_defaultTraceArguments}\".",
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
            var downloadFilesOption = app.Option("-d|--download",
                "Downloads specific server files. This argument can be used multiple times. e.g., -d \"published/wwwroot/picture.png\"", CommandOptionType.MultipleValue);
            var noCleanOption = app.Option("--no-clean",
                "Don't delete the application on the server.", CommandOptionType.NoValue);
            var fetchOption = app.Option("--fetch",
                "Downloads the published application locally.", CommandOptionType.NoValue);
            var fetchOutputOption = app.Option("--fetch-output",
                @"Can be a file prefix (app will add *.DATE*.zip) , or a specific name (end in *.zip) and no DATE* will be added e.g. --fetch-output c:\publishedapps\myApp", CommandOptionType.SingleValue);

            // ClientJob Options
            var clientThreadsOption = app.Option("--clientThreads",
                "Number of threads used by client. Default is 32.", CommandOptionType.SingleValue);
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

            app.OnExecute(() =>
            {
                _verbose = verboseOption.HasValue();

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
                var client = clientOption.Value();
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
                    string.IsNullOrWhiteSpace(client) ||
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
                    if (scenarioOption.HasValue())
                    {
                        Console.WriteLine($"Job named '{scenarioName}' was specified but no job definition argument.");
                        return 8;
                    }

                    if (!repositoryOption.HasValue() ||
                        !projectOption.HasValue())
                    {
                        Console.WriteLine($"Repository and project are mandatory when no job definition is specified.");
                        return 9;
                    }

                    jobDefinitions = new JobDefinition();
                    jobDefinitions.Add("Default", new JObject());
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
                if (!String.IsNullOrEmpty(jobOptions.PresetHeaders))
                {
                    if (!Enum.TryParse(jobOptions.PresetHeaders, ignoreCase: true, result: out headers))
                    {
                        Console.WriteLine($"Unknown KnownHeaders value: '{jobOptions.PresetHeaders}'. Choose from: None, Html, Json, Plaintext.");
                    }
                }

                // Scenario can't be set in job definitions
                serverJob.Scenario = scenarioName;
                serverJob.WebHost = webHost;

                if (databaseOption.HasValue())
                {
                    serverJob.Database = Enum.Parse<Database>(databaseOption.Value(), ignoreCase: true);
                }
                if (pathOption.HasValue())
                {
                    serverJob.Path = pathOption.Value();
                }
                if (connectionFilterOption.HasValue())
                {
                    serverJob.ConnectionFilter = connectionFilterOption.Value();
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
                if (kestrelThreadCountOption.HasValue())
                {
                    serverJob.KestrelThreadCount = int.Parse(kestrelThreadCountOption.Value());
                }
                if (argumentsOption.HasValue())
                {
                    serverJob.Arguments = argumentsOption.Value();
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
                    var split = source.IndexOf('@');
                    var repository = (split == -1) ? source : source.Substring(0, split);
                    serverJob.Source.BranchOrCommit = (split == -1) ? null : source.Substring(split + 1);

                    if (!repository.Contains(":"))
                    {
                        repository = $"https://github.com/aspnet/{repository}.git";
                    }

                    serverJob.Source.Repository = repository;
                }
                if (projectOption.HasValue())
                {
                    serverJob.Source.Project = projectOption.Value();
                }
                if (noCleanOption.HasValue())
                {
                    serverJob.NoClean = true;
                }
                if (collectTraceOption.HasValue())
                {
                    serverJob.Collect = true;
                    serverJob.CollectArguments = _defaultTraceArguments;
                    if (traceArgumentsOption.HasValue())
                    {
                        serverJob.CollectArguments = string.Join(';', serverJob.CollectArguments, traceArgumentsOption.Value());
                    }
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
                    foreach(var env in environmentVariablesOption.Values)
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
                            serverJob.EnvironmentVariables.Add(env.Substring(0, index), "");
                        }
                        else 
                        {
                            serverJob.EnvironmentVariables.Add(env.Substring(0, index), env.Substring(index + 1));
                        }
                    }
                }

                // Check all attachments exist
                if (outputFileOption.HasValue())
                {
                    foreach (var outputFile in outputFileOption.Values)
                    {
                        var fileSegments = outputFile.Split(';');
                        var filename = fileSegments[0];

                        if (!File.Exists(filename))
                        {
                            Console.WriteLine($"Output File '{filename}' could not be loaded.");
                            return 8;
                        }
                    }
                }

                if (runtimeFileOption.HasValue())
                {
                    foreach (var runtimeFile in runtimeFileOption.Values)
                    {
                        var fileSegments = runtimeFile.Split(';');
                        var filename = fileSegments[0];

                        if (!File.Exists(filename))
                        {
                            Console.WriteLine($"Runtime File '{filename}' could not be loaded.");
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

                Log($"Using worker {_clientJob.Client}");

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
                        var index = header.IndexOf('=');

                        if (index == -1)
                        {
                            Console.WriteLine($"Invalid http header, '=' not found: '{header}'");
                            return 9;
                        }

                        _clientJob.Headers[header.Substring(0, index)] = header.Substring(index + 1, header.Length - index - 1);
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
                    new Uri(client), 
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
                    runtimeFileOption,
                    markdownOption,
                    writeToFileOption,
                    requiredOperatingSystem).Result;
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

            return app.Execute(args);
        }

        private static async Task<int> Run(
            Uri serverUri,
            Uri clientUri,
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
            CommandOption runtimeFileOption,
            CommandOption markdownOption,
            CommandOption writeToFileOption,
            Benchmarks.ServerJob.OperatingSystem? requiredOperatingSystem
            )
        {
            var scenario = serverJob.Scenario;
            var serverJobsUri = new Uri(serverUri, "/jobs");
            Uri serverJobUri = null;
            HttpResponseMessage response = null;
            string responseContent = null;

            var results = new List<Statistics>();
            ClientJob clientJob = null;

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

                    serverJobUri = new Uri(serverUri, response.Headers.Location);
                    while (true)
                    {
                        LogVerbose($"GET {serverJobUri}...");
                        response = await _httpClient.GetAsync(serverJobUri);
                        responseContent = await response.Content.ReadAsStringAsync();

                        LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

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

                        if (requiredOperatingSystem.HasValue && requiredOperatingSystem.Value != serverJob.OperatingSystem)
                        {
                            Log($"Job ignored on this OS, stopping job ...");

                            response = await _httpClient.PostAsync(serverJobUri + "/stop", new StringContent(""));
                            LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");

                            return 0;
                        }

                        if (serverJob.State == ServerState.Initializing)
                        {
                            // Uploading attachments
                            if (outputFileOption.HasValue())
                            {
                                foreach (var outputFile in outputFileOption.Values)
                                {
                                    var result = await UploadFileAsync(outputFile, AttachmentLocation.Output, serverJob, serverJobUri);

                                    if (result != 0)
                                    {
                                        return result;
                                    }                                    
                                }
                            }

                            if (runtimeFileOption.HasValue())
                            {
                                foreach (var runtimeFile in runtimeFileOption.Values)
                                {
                                    var result = await UploadFileAsync(runtimeFile, AttachmentLocation.Runtime, serverJob, serverJobUri);

                                    if (result != 0)
                                    {
                                        return result;
                                    }
                                }
                            }

                            response = await _httpClient.PostAsync(serverJobUri + "/start", new StringContent(""));
                            responseContent = await response.Content.ReadAsStringAsync();
                            LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");
                            response.EnsureSuccessStatusCode();

                            Log($"Job started: {serverJobUri}");

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
                            serverBenchmarkUri = serverJob.Url;
                            break;
                        }
                        else if (serverJob.State == ServerState.Failed)
                        {
                            Log($"Job failed on benchmark server, stopping...");

                            Console.WriteLine(serverJob.Error);

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
                            return -1;
                        }
                        else
                        {
                            await Task.Delay(1000);
                        }
                    }
                    System.Threading.Thread.Sleep(200);  // Make it clear on traces when startup has finished and warmup begins.  

                    TimeSpan latencyNoLoad = TimeSpan.Zero, latencyFirstRequest = TimeSpan.Zero;

                    if (_clientJob.Warmup != 0)
                    {
                        Log("Warmup");
                        var duration = _clientJob.Duration;

                        _clientJob.Duration = _clientJob.Warmup;
                        clientJob = await RunClientJob(scenario, clientUri, serverJobUri, serverBenchmarkUri);

                        // Store the latency as measured on the warmup job
                        latencyNoLoad = clientJob.LatencyNoLoad;
                        latencyFirstRequest = clientJob.LatencyFirstRequest;
                        _clientJob.SkipStartupLatencies = false;

                        _clientJob.Duration = duration;
                        System.Threading.Thread.Sleep(200);  // Make it clear on traces when warmup stops and measuring begins. 
                    }


                    Log("Measuring");
                    var startTime = DateTime.UtcNow;
                    var spanLoop = 0;
                    var sqlTask = Task.CompletedTask;
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

                        clientJob = await RunClientJob(scenario, clientUri, serverJobUri, serverBenchmarkUri);

                        if (clientJob.State == ClientState.Completed)
                        {
                            LogVerbose($"Client Job completed");

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

                            if (clientJob.Warmup == 0)
                            {
                                latencyNoLoad = clientJob.LatencyNoLoad;
                                latencyFirstRequest = clientJob.LatencyFirstRequest;
                            }

                            var serverCounters = serverJob.ServerCounters;
                            var workingSet = Math.Round(((double)serverCounters.Select(x => x.WorkingSet).DefaultIfEmpty(0).Max()) / (1024 * 1024), 3);
                            var cpu = serverCounters.Select(x => x.CpuPercentage).DefaultIfEmpty(0).Max();

                            var statistics = new Statistics
                            {
                                RequestsPerSecond = clientJob.RequestsPerSecond,
                                LatencyOnLoad = clientJob.Latency.Average,
                                Cpu = cpu,
                                WorkingSet = workingSet,
                                StartupMain = serverJob.StartupMainMethod.TotalMilliseconds,
                                FirstRequest = latencyFirstRequest.TotalMilliseconds,
                                Latency = latencyNoLoad.TotalMilliseconds,
                                SocketErrors = clientJob.SocketErrors,
                                BadResponses = clientJob.BadResponses,

                                LatencyAverage = clientJob.Latency.Average,
                                Latency50Percentile = clientJob.Latency.Within50thPercentile,
                                Latency75Percentile = clientJob.Latency.Within75thPercentile,
                                Latency90Percentile = clientJob.Latency.Within90thPercentile,
                                Latency99Percentile = clientJob.Latency.Within99thPercentile,
                                Latency100Percentile = clientJob.Latency.Within100thPercentile,
                                TotalRequests = clientJob.Requests,
                                Duration = clientJob.ActualDuration.TotalMilliseconds
                            };

                            results.Add(statistics);

                            if (iterations > 1)
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

                            // Collect Trace
                            if (serverJob.Collect)
                            {
                                Log($"Post-processing profiler trace, this can take 10s of seconds...");
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

                                Log($"Downloading trace...");
                                if (traceDestination == null || !traceDestination.EndsWith(".etl.zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    // If it does not end with a *.etl.zip then we add a DATE.etl.zip to it
                                    if (String.IsNullOrEmpty(traceDestination))
                                    {
                                        traceDestination = "trace";
                                    }

                                    var rpsStr = "RPS-" + ((int)((statistics.RequestsPerSecond+500) / 1000)) + "K";
                                    traceDestination = traceDestination + "." + DateTime.Now.ToString("MM-dd-HH-mm-ss") + "." + rpsStr + ".etl.zip";
                                }

                                Log($"Creating trace: {traceDestination}");
                                await File.WriteAllBytesAsync(traceDestination, await _httpClient.GetByteArrayAsync(uri));

                            }

                            var shouldComputeResults = results.Any() && iterations == i;

                            if (shouldComputeResults)
                            {
                                var samples = results.OrderBy(x => x.RequestsPerSecond).Skip(exclude).SkipLast(exclude).ToList();

                                var average = new Statistics
                                {
                                    RequestsPerSecond = Math.Round(samples.Average(x => x.RequestsPerSecond)),
                                    LatencyOnLoad = Math.Round(samples.Average(x => x.LatencyOnLoad), 1),
                                    Cpu = Math.Round(samples.Average(x => x.Cpu)),
                                    WorkingSet = Math.Round(samples.Average(x => x.WorkingSet)),
                                    StartupMain = Math.Round(samples.Average(x => x.StartupMain)),
                                    FirstRequest = Math.Round(samples.Average(x => x.FirstRequest), 1),
                                    SocketErrors = Math.Round(samples.Average(x => x.SocketErrors)),
                                    BadResponses = Math.Round(samples.Average(x => x.BadResponses)),

                                    Latency = Math.Round(samples.Average(x => x.Latency), 1),
                                    LatencyAverage = Math.Round(samples.Average(x => x.LatencyAverage), 1),
                                    Latency50Percentile = Math.Round(samples.Average(x => x.Latency50Percentile), 1),
                                    Latency75Percentile = Math.Round(samples.Average(x => x.Latency75Percentile), 1),
                                    Latency90Percentile = Math.Round(samples.Average(x => x.Latency90Percentile), 1),
                                    Latency99Percentile = Math.Round(samples.Average(x => x.Latency99Percentile), 1),
                                    Latency100Percentile = Math.Round(samples.Average(x => x.Latency100Percentile), 1),
                                    TotalRequests = Math.Round(samples.Average(x => x.TotalRequests)),
                                    Duration = Math.Round(samples.Average(x => x.Duration))
                                };

                                if (serializer != null)
                                {
                                    serializer.ComputeAverages(average, samples);
                                }

                                    var fields = new List<KeyValuePair<string, string>>();
                                    if (!String.IsNullOrEmpty(description))
                                    {
                                        fields.Add(new KeyValuePair<string, string>("Description", description.ToString()));
                                    }

                                    fields.Add(new KeyValuePair<string, string>("RPS", $"{average.RequestsPerSecond:n0}"));
                                    fields.Add(new KeyValuePair<string, string>("CPU (%)", $"{average.Cpu}"));
                                    fields.Add(new KeyValuePair<string, string>("Memory (MB)",$"{average.WorkingSet:n0}"));
                                    fields.Add(new KeyValuePair<string, string>("Avg. Latency (ms)",$"{average.LatencyOnLoad}"));
                                    fields.Add(new KeyValuePair<string, string>("Startup (ms)", $"{average.StartupMain}"));
                                    fields.Add(new KeyValuePair<string, string>("First Request (ms)",$"{average.FirstRequest}"));
                                    fields.Add(new KeyValuePair<string, string>("Latency (ms)", $"{average.Latency}"));

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

                                if (markdownOption.HasValue())
                                {
                                    Log(header + "|");
                                    Log(separator + "|");
                                    Log(values + "|");
                                }
                                else
                                {
                                    Log($"RequestsPerSecond:           {average.RequestsPerSecond:n0}");
                                    Log($"Max CPU (%):                 {average.Cpu}");
                                    Log($"WorkingSet (MB):             {average.WorkingSet:n0}");
                                    Log($"Avg. Latency (ms):           {average.LatencyOnLoad}");
                                    Log($"Startup (ms):                {average.StartupMain}");
                                    Log($"First Request (ms):          {average.FirstRequest}");
                                    Log($"Latency (ms):                {average.Latency}");
                                    Log($"Total Requests:              {average.TotalRequests:n0}");
                                    Log($"Duration: (ms)               {average.Duration:n0}");
                                    Log($"Socket Errors:               {average.SocketErrors:n0}");
                                    Log($"Bad Responses:               {average.BadResponses:n0}");
                                    Log($"SDK:                         {serverJob.SdkVersion}");
                                    Log($"Runtime:                     {serverJob.RuntimeVersion}");
                                    Log($"ASP.NET Core:                {serverJob.AspNetCoreVersion}");
                                }

                                if (serializer != null && !String.IsNullOrEmpty(sqlConnectionString))
                                {
                                    sqlTask = sqlTask.ContinueWith(async t =>
                                    {
                                        Log("Writing results to SQL...");
                                        try
                                        {
                                            await serializer.WriteJobResultsToSqlAsync(
                                                serverJob: serverJob,
                                                clientJob: clientJob,
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
                        }

                        spanLoop = spanLoop + 1;
                    } while (DateTime.UtcNow - startTime < span);

                    if (!sqlTask.IsCompleted)
                    {
                        Log("Job finished, waiting for SQL to complete.");
                        await sqlTask;
                    }

                    Log($"Stopping scenario {scenario} on benchmark server...");

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

                        if (DateTime.UtcNow - jobStoppedUtc > TimeSpan.FromSeconds(10))
                        {
                            // The job needs to be deleted
                            Log($"Server didn't stop the job in the expected time, deleting it ...");
                        }

                    } while (serverJob.State != ServerState.Stopped);

                    // Download published application
                    if (fetch)
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

                                var base64 = await _httpClient.GetStringAsync(uri);
                                await File.WriteAllBytesAsync(filename, Convert.FromBase64String(base64));
                            }
                            catch (Exception e)
                            {
                                Log($"Error while downloading file {file}, skipping ...");
                                LogVerbose(e.Message);
                                continue;
                            }
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
                        Log($"Deleting scenario {scenario} on benchmark server...");

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

        private static async Task<int> UploadFileAsync(string filename, AttachmentLocation location, ServerJob serverJob, Uri serverJobUri)
        {
            try
            {
                var outputFileSegments = filename.Split(';');
                var attachmentFilename = outputFileSegments[0];

                if (!File.Exists(attachmentFilename))
                {
                    Console.WriteLine($"Output File '{attachmentFilename}' could not be loaded.");
                    return 8;
                }

                var destinationFilename = outputFileSegments.Length > 1
                    ? outputFileSegments[1]
                    : Path.GetFileName(attachmentFilename);

                var requestContent = new MultipartFormDataContent();

                var fileContent = attachmentFilename.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new StreamContent(await _httpClient.GetStreamAsync(attachmentFilename))
                    : new StreamContent(new FileStream(attachmentFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.Asynchronous | FileOptions.SequentialScan));

                requestContent.Add(fileContent, nameof(AttachmentViewModel.Content), Path.GetFileName(attachmentFilename));
                requestContent.Add(new StringContent(serverJob.Id.ToString()), nameof(AttachmentViewModel.Id));
                requestContent.Add(new StringContent(destinationFilename), nameof(AttachmentViewModel.DestinationFilename));
                requestContent.Add(new StringContent(location.ToString()), nameof(AttachmentViewModel.Location));

                await _httpClient.PostAsync(serverJobUri + "/attachment", requestContent);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"An error occured while uploading a file.", e);
            }

            return 0;
        }

        private static async Task<ClientJob> RunClientJob(string scenarioName, Uri clientUri, Uri serverJobUri, string serverBenchmarkUri)
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

        private static async Task InvokeApplicationEndpoint(Uri serverJobUri, string path)
        {
            var uri = serverJobUri + "/invoke?path=" + HttpUtility.UrlEncode(path);
            Console.WriteLine(await _httpClient.GetStringAsync(uri));
        }

        private static void QuietLog(string message)
        {
            Console.Write(message);
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }

        private static void LogVerbose(string message)
        {
            if (_verbose)
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
    }
}
