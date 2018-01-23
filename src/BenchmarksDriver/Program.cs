﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Benchmarks.ClientJob;
using Benchmarks.ServerJob;
using Microsoft.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OperatingSystem = Benchmarks.ServerJob.OperatingSystem;

namespace BenchmarksDriver
{
    public class Program
    {
        private static bool _verbose;

        private static readonly HttpClient _httpClient = new HttpClient();
        
        private static ClientJob _clientJob;
        
        public static int Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                Name = "BenchmarksDriver",
                FullName = "ASP.NET Benchmark Driver",
                Description = "Driver for ASP.NET Benchmarks"
            };

            app.HelpOption("-?|-h|--help");

            // Driver Options
            var clientOption = app.Option("-c|--client",
                "URL of benchmark client", CommandOptionType.SingleValue);
            var serverOption = app.Option("-s|--server",
                "URL of benchmark server", CommandOptionType.SingleValue);
            var sqlConnectionStringOption = app.Option("-q|--sql",
                "Connection string of SQL Database to store results", CommandOptionType.SingleValue);
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
                "An endpoint to call before the application is shutdown.", CommandOptionType.SingleValue);

            // ServerJob Options
            var databaseOption = app.Option("--database",
                "The type of database to run the benchmarks with (PostgreSql, SqlServer or MySql). Default is PostgreSql.", CommandOptionType.SingleValue);
            var connectionFilterOption = app.Option("-f|--connectionFilter",
                "Assembly-qualified name of the ConnectionFilter", CommandOptionType.SingleValue);
            var kestrelThreadCountOption = app.Option("--kestrelThreadCount",
                "Maps to KestrelServerOptions.ThreadCount.",
                CommandOptionType.SingleValue);
            var scenarioOption = app.Option("-n|--scenario",
                "Benchmark scenario to run", CommandOptionType.SingleValue);
            var schemeOption = app.Option("-m|--scheme",
                "Scheme (http or https).  Default is http.", CommandOptionType.SingleValue);
            var sourceOption = app.Option("-o|--source",
                "Source dependency. Format is 'repo@branchOrCommit'. " +
                "Repo can be a full URL, or a short name under https://github.com/aspnet.",
                CommandOptionType.MultipleValue);
            var webHostOption = app.Option(
                "-w|--webHost",
                "WebHost (e.g., KestrelLibuv, KestrelSockets, HttpSys). Default is KestrelSockets.",
                CommandOptionType.SingleValue);
            var aspnetCoreVersionOption = app.Option("--aspnetCoreVersion",
                "ASP.NET Core packages version (Current, Latest, or custom value). Current is the latest public version, Latest is the currently developped one. Default is Latest (2.1.0-*).", CommandOptionType.SingleValue);
            var runtimeVersionOption = app.Option("--runtimeVersion",
                ".NET Core Runtime version (Current, Latest, or custom value). Current is the latest public version, Latest is the currently developped one. Default is Latest (2.1.0-*).", CommandOptionType.SingleValue);
            var argumentsOption = app.Option("--arguments",
                "Arguments to pass to the application. (e.g., \"--raw true\")", CommandOptionType.SingleValue);
            var portOption = app.Option("--port",
                "The port used to request the benchmarked application. Default is 5000.", CommandOptionType.SingleValue);
            var repositoryOption = app.Option("-r|--repository",
                "Git repository containing the project to test.", CommandOptionType.SingleValue);
            var projectOption = app.Option("--projectFile",
                "Relative path of the project to test in the repository. (e.g., \"src/Benchmarks/Benchmarks.csproj)\"", CommandOptionType.SingleValue);
            var useRuntimeStoreOption = app.Option("--useRuntimeStore",
                "Runs the benchmarks using the runtime store if available.", CommandOptionType.NoValue);
            var timeoutOption = app.Option("--timeout",
                "The max delay to wait to the job to run. Default is 00:05:00.", CommandOptionType.SingleValue);
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
                "Collect a PerfView trace. Optionally set custom arguments. e.g., BufferSize=256;InMemoryCircularBuffer", CommandOptionType.NoValue);

            // ClientJob Options
            var clientThreadsOption = app.Option("--clientThreads",
                "Number of threads used by client. Default is 32.", CommandOptionType.SingleValue);
            var connectionsOption = app.Option("--connections",
                "Number of connections used by client. Default is 256.", CommandOptionType.SingleValue);
            var durationOption = app.Option("--duration",
                "Duration of test in seconds. Default is 15.", CommandOptionType.SingleValue);
            var headerOption = app.Option("--header",
                "Header added to request.", CommandOptionType.MultipleValue);
            var headersOption = app.Option("--headers",
                "Default set of HTTP headers added to request (None, Plaintext, Json, Html). Default is Html.", CommandOptionType.SingleValue);
            var methodOption = app.Option("--method",
                "HTTP method of the request. Default is GET.", CommandOptionType.SingleValue);
            var scriptNameOption = app.Option("--script",
                "Name of the script used by wrk.", CommandOptionType.SingleValue);
            var pipelineDepthOption = app.Option("--pipelineDepth",
                "Depth of pipeline used by client.", CommandOptionType.SingleValue);
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
                

                var server = serverOption.Value();
                var client = clientOption.Value();
                var headers = Headers.Html;
                var jobDefinitionPathOrUrl =  jobsOptions.Value();
                var iterations = 1;
                var exclude = 0;

                var sqlConnectionString = sqlConnectionStringOption.Value();

                if (!Enum.TryParse(schemeValue, ignoreCase: true, result: out Scheme scheme) ||
                    !Enum.TryParse(webHostValue, ignoreCase: true, result: out WebHost webHost) ||
                    (headersOption.HasValue() && !Enum.TryParse(headersOption.Value(), ignoreCase: true, result: out headers)) ||
                    (databaseOption.HasValue() && !Enum.TryParse(databaseOption.Value(), ignoreCase: true, result: out Database database)) ||
                    string.IsNullOrWhiteSpace(server) ||
                    string.IsNullOrWhiteSpace(client) ||
                    (timeoutOption.HasValue() && !TimeSpan.TryParse(timeoutOption.Value(), result: out TimeSpan timeoutValue)) ||
                    (iterationsOption.HasValue() && !int.TryParse(iterationsOption.Value(), result: out iterations)) ||
                    (excludeOption.HasValue() && !int.TryParse(excludeOption.Value(), result: out exclude)))
                {
                    app.ShowHelp();
                    return 2;
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
                        return 8;
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
                if (webHostOption.HasValue())
                {
                    serverJob.WebHost = webHost;
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
                if (timeoutOption.HasValue())
                {
                    serverJob.Timeout = timeoutValue;
                }
                if (collectTraceOption.HasValue())
                {
                    serverJob.Collect = true;
                    // Increase server job timeout as Perfview collection can be time consuming
                    if (!timeoutOption.HasValue())
                    {
                        serverJob.Timeout = TimeSpan.FromMinutes(10);
                    }

                    serverJob.CollectArguments = collectTraceOption.Value();

                    // Clear the arguments if the value is "on" as this is a marker for NoValue on the command parser
                    if (serverJob.CollectArguments == "on")
                    {
                        serverJob.CollectArguments = "";
                    }
                }

                var attachments = new List<Attachment>();

                if (outputFileOption.HasValue())
                {
                    foreach (var outputFile in outputFileOption.Values)
                    {
                        var attachment = new Attachment { Location = AttachmentLocation.Output };

                        try
                        {
                            var outputFileSegments = outputFile.Split(';');

                            attachment.Filename = outputFileSegments[0];
                            
                            if (attachment.Filename.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                attachment.Content = _httpClient.GetByteArrayAsync(attachment.Filename).GetAwaiter().GetResult();
                            }
                            else
                            {
                                attachment.Content = File.ReadAllBytes(attachment.Filename);
                            }

                            attachment.Filename = Path.GetFileName(attachment.Filename);

                            if (outputFileSegments.Length > 1)
                            {
                                attachment.Filename = outputFileSegments[1];
                            }
                            else
                            {
                                attachment.Filename = Path.GetFileName(attachment.Filename);
                            }

                            attachments.Add(attachment);
                        }
                        catch
                        {
                            Console.WriteLine($"Output File '{attachment.Filename}' could not be loaded.");
                            return 8;
                        }
                    }
                }

                if (runtimeFileOption.HasValue())
                {
                    foreach (var runtimeFile in runtimeFileOption.Values)
                    {
                        var attachment = new Attachment { Location = AttachmentLocation.Runtime };

                        try
                        {
                            var runtimeFileSegments = runtimeFile.Split(';');

                            attachment.Filename = runtimeFileSegments[0];

                            if (attachment.Filename.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                attachment.Content = _httpClient.GetByteArrayAsync(attachment.Filename).GetAwaiter().GetResult();
                            }
                            else
                            {
                                attachment.Content = File.ReadAllBytes(attachment.Filename);
                            }

                            attachment.Filename = Path.GetFileName(attachment.Filename);

                            if (runtimeFileSegments.Length > 1)
                            {
                                attachment.Filename = runtimeFileSegments[1];
                            }
                            else
                            {
                                attachment.Filename = Path.GetFileName(attachment.Filename);
                            }

                            attachments.Add(attachment);
                        }
                        catch
                        {
                            Console.WriteLine($"Runtime File '{attachment.Filename}' could not be loaded.");
                            return 8;
                        }
                    }
                }

                serverJob.Attachments = attachments.ToArray();

                foreach (var source in sourceOption.Values)
                {
                    var split = source.IndexOf('@');
                    var repository = (split == -1) ? source : source.Substring(0, split);
                    var branch = (split == -1) ? null : source.Substring(split + 1);

                    if (!repository.Contains(":"))
                    {
                        repository = $"https://github.com/aspnet/{repository}.git";
                    }

                    serverJob.ReferenceSources.Add(new Source() { BranchOrCommit = branch, Repository = repository });
                }

                // Building ClientJob

                var mergedClientJob = new JObject(defaultJob);
                mergedClientJob.Merge(job);
                _clientJob = mergedClientJob.ToObject<ClientJob>();

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
                if (pipelineDepthOption.HasValue())
                {
                    _clientJob.PipelineDepth = int.Parse(pipelineDepthOption.Value());

                    if (_clientJob.PipelineDepth > 0)
                    {
                        _clientJob.ScriptName = "pipeline";
                    }
                }
                if (scriptNameOption.HasValue())
                {
                    _clientJob.ScriptName = scriptNameOption.Value();
                }
                if (methodOption.HasValue())
                {
                    _clientJob.Method = methodOption.Value();
                }
                if (querystringOption.HasValue())
                {
                    _clientJob.Query = querystringOption.Value();
                }

                switch (headers)
                {
                    case Headers.None:
                        break;

                    case Headers.Html:
                        _clientJob.Headers["Host"] = "localhost";
                        _clientJob.Headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
                        _clientJob.Headers["Connection"] = "keep-alive";
                        break;

                    case Headers.Json:
                        _clientJob.Headers["Host"] = "localhost";
                        _clientJob.Headers["Accept"] = "application/json,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7";
                        _clientJob.Headers["Connection"] = "keep-alive";
                        break;

                    case Headers.Plaintext:
                        _clientJob.Headers["Host"] = "localhost";
                        _clientJob.Headers["Accept"] = "text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7";
                        _clientJob.Headers["Connection"] = "keep-alive";
                        break;
                }

                if (headerOption.HasValue())
                {
                    foreach(var header in headerOption.Values)
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

                return Run(new Uri(server), new Uri(client), sqlConnectionString, serverJob, session, description, iterations, exclude, shutdownOption.Value()).Result;
            });

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
            string shutdownEndpoint)
        {
            var scenario = serverJob.Scenario;
            var serverJobsUri = new Uri(serverUri, "/jobs");
            Uri serverJobUri = null;
            HttpResponseMessage response = null;
            string responseContent = null;

            var results = new List<Statistics>();
            ClientJob clientJob = null;

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

                    var serverBenchmarkUri = (string)null;
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

                        if (serverJob.State == ServerState.Running)
                        {
                            serverBenchmarkUri = serverJob.Url;
                            break;
                        }
                        else if (serverJob.State == ServerState.Failed)
                        {
                            throw new InvalidOperationException("Server job failed");
                        }
                        else if (serverJob.State == ServerState.NotSupported)
                        {
                            Log("Server does not support this job configuration.");
                            return 0;
                        }
                        else
                        {
                            await Task.Delay(1000);
                        }
                    }

                    Log("Warmup");
                    clientJob = await RunClientJob(scenario, clientUri, serverJobUri, serverBenchmarkUri);

                    Log("Benchmark");
                    clientJob = await RunClientJob(scenario, clientUri, serverJobUri, serverBenchmarkUri);

                    if (clientJob.State == ClientState.Completed)
                    {
                        if (i == iterations && !String.IsNullOrEmpty(shutdownEndpoint))
                        {
                            Log($"Invoking '{shutdownEndpoint}' on benchmarked application...");
                            await InvokeApplicationEndpoint(serverJobUri, shutdownEndpoint);
                        }

                        // Load latest state of server job
                        LogVerbose($"GET {serverJobUri}...");
                        response = await _httpClient.GetAsync(serverJobUri);
                        responseContent = await response.Content.ReadAsStringAsync();

                        LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

                        serverJob = JsonConvert.DeserializeObject<ServerJob>(responseContent);

                        var workingSet = Math.Round(((double)serverJob.ServerCounters.Select(x => x.WorkingSet).DefaultIfEmpty(0).Max()) / (1024 * 1024), 3);
                        var cpu = serverJob.ServerCounters.Select(x => x.CpuPercentage).DefaultIfEmpty(0).Max();

                        var statistics = new Statistics
                        {
                            RequestsPerSecond = clientJob.RequestsPerSecond,
                            LatencyOnLoad = clientJob.Latency.Average.TotalMilliseconds,
                            Cpu = cpu,
                            WorkingSet = workingSet,
                            StartupMain = serverJob.StartupMainMethod.TotalMilliseconds,
                            FirstRequest = clientJob.LatencyFirstRequest.TotalMilliseconds,
                            Latency = clientJob.LatencyNoLoad.TotalMilliseconds,
                            SocketErrors = clientJob.SocketErrors,
                            BadResponses = clientJob.BadResponses,

                            LatencyAverage = clientJob.Latency.Average.TotalMilliseconds,
                            Latency50Percentile = clientJob.Latency.Within50thPercentile.TotalMilliseconds,
                            Latency75Percentile = clientJob.Latency.Within75thPercentile.TotalMilliseconds,
                            Latency90Percentile = clientJob.Latency.Within90thPercentile.TotalMilliseconds,
                            Latency99Percentile = clientJob.Latency.Within99thPercentile.TotalMilliseconds,
                            TotalRequests = clientJob.Requests,
                            Duration = clientJob.ActualDuration.TotalMilliseconds

                        };

                        results.Add(statistics);

                        if (iterations > 1)
                        {
                            LogVerbose($"RequestsPerSecond:           {statistics.RequestsPerSecond}");
                            LogVerbose($"Latency on load (ms):        {statistics.LatencyOnLoad}");
                            LogVerbose($"Max CPU (%):                 {statistics.Cpu}");
                            LogVerbose($"WorkingSet (MB):             {statistics.WorkingSet}");
                            LogVerbose($"Startup Main (ms):           {statistics.StartupMain}");
                            LogVerbose($"First Request (ms):          {statistics.FirstRequest}");
                            LogVerbose($"Latency (ms):                {statistics.Latency}");
                            LogVerbose($"Socket Errors:               {statistics.SocketErrors}");
                            LogVerbose($"Bad Responses:               {statistics.BadResponses}");
                        }

                        // Collect Trace
                        if (serverJob.Collect)
                        {
                            Log($"Collecting trace, this can take 10s of seconds...");
                            var uri = serverJobUri + "/trace";
                            response = await _httpClient.PostAsync(uri, new StringContent(""));
                            response.EnsureSuccessStatusCode();
                            
                            while (true)
                            {
                                LogVerbose($"GET {serverJobUri}...");
                                response = await _httpClient.GetAsync(serverJobUri);
                                responseContent = await response.Content.ReadAsStringAsync();

                                LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

                                serverJob = JsonConvert.DeserializeObject<ServerJob>(responseContent);

                                if (serverJob == null)
                                {
                                    Log($"The job was forcibly stopped by the server.");
                                    return 1;
                                }

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

                            var filename = "trace.etl.zip";
                            var counter = 1;
                            while (File.Exists(filename))
                            {
                                filename = $"trace({counter++}).etl.zip";
                            }

                            await File.WriteAllBytesAsync(filename, await _httpClient.GetByteArrayAsync(uri));
                        }
                    }
                }
                catch(Exception e)
                {
                    Log($"Interrupting due to an unexpected exception");
                    Log(e.ToString());
                }
                finally
                {
                    if (serverJobUri != null)
                    {
                        Log($"Stopping scenario {scenario} on benchmark server...");

                        LogVerbose($"DELETE {serverJobUri}...");
                        response = _httpClient.DeleteAsync(serverJobUri).Result;
                        LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");

                        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
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

            if (results.Any())
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
                    TotalRequests = Math.Round(samples.Average(x => x.TotalRequests)),
                    Duration = Math.Round(samples.Average(x => x.Duration))
                };

                Log($"RequestsPerSecond:           {average.RequestsPerSecond}");
                Log($"Latency on load (ms):        {average.LatencyOnLoad}");
                Log($"Max CPU (%):                 {average.Cpu}");
                Log($"WorkingSet (MB):             {average.WorkingSet}");
                Log($"Startup Main (ms):           {average.StartupMain}");
                Log($"First Request (ms):          {average.FirstRequest}");
                Log($"Latency (ms):                {average.Latency}");
                Log($"Socket Errors:               {average.SocketErrors}");
                Log($"Bad Responses:               {average.BadResponses}");

                if (!string.IsNullOrWhiteSpace(sqlConnectionString))
                {
                    Log("Writing results to SQL...");

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "RequestsPerSecond",
                        value: average.RequestsPerSecond);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "Startup Main (ms)",
                        value: average.StartupMain);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "First Request (ms)",
                        value: average.FirstRequest);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "WorkingSet (MB)",
                        value: average.WorkingSet);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "CPU",
                        value: average.Cpu);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        session: session,
                        description: description,
                        path: serverJob.Path,
                        dimension: "Latency (ms)",
                        value: average.Latency);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "LatencyAverage (ms)",
                        value: average.LatencyAverage);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "Latency50Percentile (ms)",
                        value: average.Latency50Percentile);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "Latency75Percentile (ms)",
                        value: average.Latency75Percentile);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "Latency90Percentile (ms)",
                        value: average.Latency90Percentile);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "Latency99Percentile (ms)",
                        value: average.Latency99Percentile);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "SocketErrors",
                        value: average.SocketErrors);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "BadResponses",
                        value: average.BadResponses);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "TotalRequests",
                        value: average.TotalRequests);

                    await WriteJobsToSql(
                        serverJob: serverJob,
                        clientJob: clientJob,
                        connectionString: sqlConnectionString,
                        path: serverJob.Path,
                        session: session,
                        description: description,
                        dimension: "Duration (ms)",
                        value: average.Duration);
                }
            }

            return 0;
        }

        private static async Task<ClientJob> RunClientJob(string scenarioName, Uri clientUri, Uri serverJobUri, string serverBenchmarkUri)
        {
            var clientJob = new ClientJob(_clientJob) { ServerBenchmarkUri = serverBenchmarkUri };

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
                    LogVerbose($"GET {clientJobUri}...");
                    response = await _httpClient.GetAsync(clientJobUri);
                    responseContent = await response.Content.ReadAsStringAsync();

                    // Ping server job to keep it alive
                    response = await _httpClient.GetAsync(serverJobUri);

                    LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

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
                    LogVerbose($"GET {clientJobUri}...");
                    response = await _httpClient.GetAsync(clientJobUri);
                    responseContent = await response.Content.ReadAsStringAsync();

                    LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

                    clientJob = JsonConvert.DeserializeObject<ClientJob>(responseContent);

                    if (clientJob.State == ClientState.Completed)
                    {
                        Log($"Scenario {scenarioName} completed on benchmark client");
                        LogVerbose($"Output: {clientJob.Output}");

                        if (!String.IsNullOrWhiteSpace(clientJob.Error))
                        {
                            LogVerbose($"Error: {clientJob.Error}");
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
                    Log($"Stopping scenario {scenarioName} on benchmark client...");

                    LogVerbose($"DELETE {clientJobUri}...");
                    var response = _httpClient.DeleteAsync(clientJobUri).Result;
                    LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");
                    response.EnsureSuccessStatusCode();
                }
            }

            return clientJob;
        }

        private static async Task InvokeApplicationEndpoint(Uri serverJobUri, string path)
        {
            var uri = serverJobUri + "/invoke?path=" + HttpUtility.UrlEncode(path);
            Console.WriteLine(await _httpClient.GetStringAsync(uri));
        }

        private static Task WriteJobsToSql(ServerJob serverJob, ClientJob clientJob, string connectionString, string path, string session, string description, string dimension, double value)
        {
            return WriteResultsToSql(
                        connectionString: connectionString,
                        scenario: serverJob.Scenario,
                        session: session,
                        description: description,
                        aspnetCoreVersion: serverJob.AspNetCoreVersion,
                        runtimeVersion: serverJob.RuntimeVersion,
                        hardware: serverJob.Hardware.Value,
                        hardwareVersion: serverJob.HardwareVersion,
                        operatingSystem: serverJob.OperatingSystem.Value,
                        scheme: serverJob.Scheme,
                        sources: serverJob.ReferenceSources,
                        connectionFilter: serverJob.ConnectionFilter,
                        webHost: serverJob.WebHost,
                        kestrelThreadCount: serverJob.KestrelThreadCount,
                        clientThreads: clientJob.Threads,
                        connections: clientJob.Connections,
                        duration: clientJob.Duration,
                        pipelineDepth: clientJob.PipelineDepth,
                        path: path,
                        method: clientJob.Method,
                        headers: clientJob.Headers,
                        dimension: dimension,
                        value: value,
                        runtimeStore: serverJob.UseRuntimeStore);
        }
        private static async Task WriteResultsToSql(
            string connectionString,
            string session,
            string description,
            string aspnetCoreVersion,
            string runtimeVersion,
            string scenario,
            Hardware hardware,
            string hardwareVersion,
            OperatingSystem operatingSystem,
            Scheme scheme,
            IEnumerable<Source> sources,
            string connectionFilter,
            WebHost webHost,
            int? kestrelThreadCount,
            int clientThreads,
            int connections,
            int duration,
            int? pipelineDepth,
            string path,
            string method,
            IDictionary<string, string> headers,
            string dimension,
            double value,
            bool runtimeStore)
        {
            const string createCmd =
                @"
                IF OBJECT_ID(N'dbo.AspNetBenchmarks', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[AspNetBenchmarks](
                        [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [Excluded] [bit] DEFAULT 0,
                        [DateTime] [datetimeoffset](7) NOT NULL,
                        [Session] [nvarchar](max) NOT NULL,
                        [Description] [nvarchar](max),
                        [AspNetCoreVersion] [nvarchar](max) NOT NULL,
                        [RuntimeVersion] [nvarchar](max) NOT NULL,
                        [Scenario] [nvarchar](max) NOT NULL,
                        [Hardware] [nvarchar](max) NOT NULL,
                        [HardwareVersion] [nvarchar](128) NOT NULL,
                        [OperatingSystem] [nvarchar](max) NOT NULL,
                        [Framework] [nvarchar](max) NOT NULL,
                        [RuntimeStore] [bit] NOT NULL,
                        [Scheme] [nvarchar](max) NOT NULL,
                        [Sources] [nvarchar](max) NULL,
                        [ConnectionFilter] [nvarchar](max) NULL,
                        [WebHost] [nvarchar](max) NOT NULL,
                        [KestrelThreadCount] [int] NULL,
                        [ClientThreads] [int] NOT NULL,
                        [Connections] [int] NOT NULL,
                        [Duration] [int] NOT NULL,
                        [PipelineDepth] [int] NULL,
                        [Path] [nvarchar](max) NULL,
                        [Method] [nvarchar](max) NOT NULL,
                        [Headers] [nvarchar](max) NULL,
                        [Dimension] [nvarchar](max) NOT NULL,
                        [Value] [float] NOT NULL
                    )
                END
                ";

            const string insertCmd =
                @"
                INSERT INTO [dbo].[AspNetBenchmarks]
                           ([DateTime]
                           ,[Session]
                           ,[Description]
                           ,[AspNetCoreVersion]
                           ,[RuntimeVersion]
                           ,[Scenario]
                           ,[Hardware]
                           ,[HardwareVersion]
                           ,[OperatingSystem]
                           ,[Framework]
                           ,[RuntimeStore]
                           ,[Scheme]
                           ,[Sources]
                           ,[ConnectionFilter]
                           ,[WebHost]
                           ,[KestrelThreadCount]
                           ,[ClientThreads]
                           ,[Connections]
                           ,[Duration]
                           ,[PipelineDepth]
                           ,[Path]
                           ,[Method]
                           ,[Headers]
                           ,[Dimension]
                           ,[Value])
                     VALUES
                           (@DateTime
                           ,@Session
                           ,@Description
                           ,@AspNetCoreVersion
                           ,@RuntimeVersion
                           ,@Scenario
                           ,@Hardware
                           ,@HardwareVersion
                           ,@OperatingSystem
                           ,@Framework
                           ,@RuntimeStore
                           ,@Scheme
                           ,@Sources
                           ,@ConnectionFilter
                           ,@WebHost
                           ,@KestrelThreadCount
                           ,@ClientThreads
                           ,@Connections
                           ,@Duration
                           ,@PipelineDepth
                           ,@Path
                           ,@Method
                           ,@Headers
                           ,@Dimension
                           ,@Value)
                ";

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqlCommand(createCmd, connection))
                {
                    await command.ExecuteNonQueryAsync();
                }

                using (var command = new SqlCommand(insertCmd, connection))
                {
                    var p = command.Parameters;
                    p.AddWithValue("@DateTime", DateTimeOffset.UtcNow);
                    p.AddWithValue("@Session", session);
                    p.AddWithValue("@Description", description);
                    p.AddWithValue("@AspNetCoreVersion", aspnetCoreVersion);
                    p.AddWithValue("@RuntimeVersion", aspnetCoreVersion);
                    p.AddWithValue("@Scenario", scenario.ToString());
                    p.AddWithValue("@Hardware", hardware.ToString());
                    p.AddWithValue("@HardwareVersion", hardwareVersion);
                    p.AddWithValue("@OperatingSystem", operatingSystem.ToString());
                    p.AddWithValue("@Framework", "Core");
                    p.AddWithValue("@RuntimeStore", runtimeStore);
                    p.AddWithValue("@Scheme", scheme.ToString().ToLowerInvariant());
                    p.AddWithValue("@Sources", sources.Any() ? (object)ConvertToSqlString(sources) : DBNull.Value);
                    p.AddWithValue("@ConnectionFilter",
                        string.IsNullOrEmpty(connectionFilter) ? (object)DBNull.Value : connectionFilter);
                    p.AddWithValue("@WebHost", webHost.ToString());
                    p.AddWithValue("@KestrelThreadCount", (object)kestrelThreadCount ?? DBNull.Value);
                    p.AddWithValue("@ClientThreads", clientThreads);
                    p.AddWithValue("@Connections", connections);
                    p.AddWithValue("@Duration", duration);
                    p.AddWithValue("@PipelineDepth", (object)pipelineDepth ?? DBNull.Value);
                    p.AddWithValue("@Path", string.IsNullOrEmpty(path) ? (object)DBNull.Value : path);
                    p.AddWithValue("@Method", method.ToString().ToUpperInvariant());
                    p.AddWithValue("@Headers", headers.Any() ? JsonConvert.SerializeObject(headers) : (object)DBNull.Value);
                    p.AddWithValue("@Dimension", dimension);
                    p.AddWithValue("@Value", value);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private static string ConvertToSqlString(IEnumerable<Source> sources)
        {
            return string.Join(",", sources.Select(s => ConvertToSqlString(s)));
        }

        private static string ConvertToSqlString(Source source)
        {
            const string aspnetPrefix = "https://github.com/aspnet/";
            const string gitSuffix = ".git";

            var shortRepository = source.Repository;

            if (shortRepository.StartsWith(aspnetPrefix))
            {
                shortRepository = shortRepository.Substring(aspnetPrefix.Length);
            }

            if (shortRepository.EndsWith(gitSuffix))
            {
                shortRepository = shortRepository.Substring(0, shortRepository.Length - gitSuffix.Length);
            }

            if (string.IsNullOrEmpty(source.BranchOrCommit))
            {
                return shortRepository;
            }
            else
            {
                return shortRepository + "@" + source.BranchOrCommit;
            }
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
    }
}
