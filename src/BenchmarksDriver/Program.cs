// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
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

            // ServerJob Options
            var connectionFilterOption = app.Option("-f|--connectionFilter",
                "Assembly-qualified name of the ConnectionFilter", CommandOptionType.SingleValue);
            var kestrelThreadCountOption = app.Option("--kestrelThreadCount",
                "Maps to KestrelServerOptions.ThreadCount.",
                CommandOptionType.SingleValue);
            var kestrelThreadPoolDispatchingOption = app.Option("--kestrelThreadPoolDispatching",
                "Maps to InternalKestrelServerOptions.ThreadPoolDispatching.",
                CommandOptionType.SingleValue);
            var kestrelTransportOption = app.Option("--kestrelTransport",
                "Kestrel's transport (Libuv or Sockets). Default is Libuv.",
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
                "WebHost (Kestrel or HttpSys). Default is Kestrel.",
                CommandOptionType.SingleValue);
            var aspnetCoreVersionOption = app.Option("--aspnetCoreVersion",
                "ASP.NET Core version (2.0.0, 2.0.1 or 2.1.0-*).  Default is 2.1.0-*.", CommandOptionType.SingleValue);
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

            // ClientJob Options
            var clientThreadsOption = app.Option("--clientThreads",
                "Number of threads used by client.", CommandOptionType.SingleValue);
            var connectionsOption = app.Option("--connections",
                "Number of connections used by client.", CommandOptionType.SingleValue);
            var durationOption = app.Option("--duration",
                "Duration of test in seconds.", CommandOptionType.SingleValue);
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
                    webHostValue = "Kestrel";
                }

                var aspnetCoreVersion = aspnetCoreVersionOption.Value();
                if (string.IsNullOrEmpty(aspnetCoreVersion))
                {
                    aspnetCoreVersion = "2.1.0-*";
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

                var sqlConnectionString = sqlConnectionStringOption.Value();

                if (!Enum.TryParse(schemeValue, ignoreCase: true, result: out Scheme scheme) ||
                    !Enum.TryParse(webHostValue, ignoreCase: true, result: out WebHost webHost) ||
                    (headersOption.HasValue() && !Enum.TryParse(headersOption.Value(), ignoreCase: true, result: out headers)) ||
                    string.IsNullOrWhiteSpace(server) ||
                    string.IsNullOrWhiteSpace(client))
                {
                    app.ShowHelp();
                    return 2;
                }

                var scenarioName = scenarioOption.Value() ?? "Default";
                Dictionary<string, JObject> jobDefinitions;

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

                    jobDefinitions = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(jobDefinitionContent);

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

                    jobDefinitions = new Dictionary<string, JObject>();
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

                if (pathOption.HasValue())
                {
                    serverJob.Path = pathOption.Value();
                }

                // These properties can't be set in the job definitions
                serverJob.Scenario = scenarioName;
                serverJob.AspNetCoreVersion = aspnetCoreVersion;

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
                if (kestrelTransportOption.HasValue())
                {
                    if (!Enum.TryParse(kestrelTransportOption.Value(), ignoreCase: true, result: out KestrelTransport kestrelTransport))
                    {
                        app.ShowHelp();
                        return 2;
                    }
                    serverJob.KestrelTransport = kestrelTransport;
                }
                if (kestrelThreadCountOption.HasValue())
                {
                    serverJob.KestrelThreadCount = int.Parse(kestrelThreadCountOption.Value());
                }
                if (kestrelThreadPoolDispatchingOption.HasValue())
                {
                    serverJob.KestrelThreadPoolDispatching = bool.Parse(kestrelThreadPoolDispatchingOption.Value());
                }
                if (argumentsOption.HasValue())
                {
                    serverJob.Arguments = argumentsOption.Value();
                }
                if (portOption.HasValue())
                {
                    serverJob.Port = int.Parse(portOption.Value());
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
                if (headersOption.HasValue())
                {
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

                return Run(new Uri(server), new Uri(client), sqlConnectionString, serverJob, session, description).Result;
            });

            return app.Execute(args);
        }

        private static async Task<int> Run(
            Uri serverUri,
            Uri clientUri,
            string sqlConnectionString,
            ServerJob serverJob,
            string session,
            string description)
        {
            var scenario = serverJob.Scenario;
            var serverJobsUri = new Uri(serverUri, "/jobs");
            Uri serverJobUri = null;
            HttpResponseMessage response = null;
            string responseContent = null;

            try
            {
                Log($"Running session '{session}' with description '{description}'");
                Log($"Starting scenario {scenario} on benchmark server...");

                var content = JsonConvert.SerializeObject(serverJob);
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
                await RunClientJob(scenario, clientUri, serverBenchmarkUri);

                Log("Benchmark");
                var clientJob = await RunClientJob(scenario, clientUri, serverBenchmarkUri);

                if (clientJob.State == ClientState.Completed)
                {
                    // Load latest state of server job
                    LogVerbose($"GET {serverJobUri}...");
                    response = await _httpClient.GetAsync(serverJobUri);
                    responseContent = await response.Content.ReadAsStringAsync();

                    LogVerbose($"{(int)response.StatusCode} {response.StatusCode} {responseContent}");

                    serverJob = JsonConvert.DeserializeObject<ServerJob>(responseContent);

                    var workingSet = Math.Round(((double)serverJob.ServerCounters.Select(x => x.WorkingSet).Max()) / (1024 * 1024), 3);
                    var cpu = serverJob.ServerCounters.Select(x => x.CpuPercentage).Max();

                    Log($"RequestsPerSecond:           {clientJob.RequestsPerSecond}");
                    Log($"Latency on load (ms):        {clientJob.Latency.Average.TotalMilliseconds}");
                    Log($"Max CPU (%):                 {cpu}");
                    Log($"WorkingSet (MB):             {workingSet}");
                    Log($"Startup Main (ms):           {serverJob.StartupMainMethod.TotalMilliseconds}");
                    Log($"First Request (ms):          {clientJob.LatencyFirstRequest.TotalMilliseconds}");
                    Log($"Latency (ms):                {clientJob.LatencyNoLoad.TotalMilliseconds}");
                    
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
                            value: clientJob.RequestsPerSecond);
                            
                        await WriteJobsToSql(
                            serverJob: serverJob, 
                            clientJob: clientJob,
                            connectionString: sqlConnectionString,
                            path: serverJob.Path,
                            session: session,
                            description: description,
                            dimension: "Startup Main (ms)",
                            value: serverJob.StartupMainMethod.TotalMilliseconds);

                        await WriteJobsToSql(
                            serverJob: serverJob, 
                            clientJob: clientJob,
                            connectionString: sqlConnectionString,
                            path: serverJob.Path,
                            session: session,
                            description: description,
                            dimension: "First Request (ms)",
                            value: clientJob.LatencyFirstRequest.TotalMilliseconds);

                        await WriteJobsToSql(
                            serverJob: serverJob, 
                            clientJob: clientJob,
                            connectionString: sqlConnectionString,
                            path: serverJob.Path,
                            session: session,
                            description: description,
                            dimension: "WorkingSet (MB)",
                            value: workingSet);

                        await WriteJobsToSql(
                            serverJob: serverJob, 
                            clientJob: clientJob,
                            connectionString: sqlConnectionString,
                            path: serverJob.Path,
                            session: session,
                            description: description,
                            dimension: "CPU",
                            value: cpu);

                        await WriteJobsToSql(
                            serverJob: serverJob, 
                            clientJob: clientJob,
                            connectionString: sqlConnectionString,
                            session: session,
                            description: description,
                            path: serverJob.Path,
                            dimension: "Latency (ms)",
                            value: clientJob.LatencyNoLoad.TotalMilliseconds);

                        await WriteJobsToSql(
                            serverJob: serverJob, 
                            clientJob: clientJob,
                            connectionString: sqlConnectionString,
                            path: serverJob.Path,
                            session: session,
                            description: description,
                            dimension: "LatencyAverage (ms)",
                            value: clientJob.Latency.Average.TotalMilliseconds);
                        
                        await WriteJobsToSql(
                            serverJob: serverJob, 
                            clientJob: clientJob,
                            connectionString: sqlConnectionString,
                            path: serverJob.Path,
                            session: session,
                            description: description,
                            dimension: "Latency50Percentile (ms)",
                            value: clientJob.Latency.Within50thPercentile.TotalMilliseconds);
                        
                        await WriteJobsToSql(
                            serverJob: serverJob, 
                            clientJob: clientJob,
                            connectionString: sqlConnectionString,
                            path: serverJob.Path,
                            session: session,
                            description: description,
                            dimension: "Latency75Percentile (ms)",
                            value: clientJob.Latency.Within75thPercentile.TotalMilliseconds);
                        
                        await WriteJobsToSql(
                            serverJob: serverJob, 
                            clientJob: clientJob,
                            connectionString: sqlConnectionString,
                            path: serverJob.Path,
                            session: session,
                            description: description,
                            dimension: "Latency90Percentile (ms)",
                            value: clientJob.Latency.Within90thPercentile.TotalMilliseconds);
                        
                        await WriteJobsToSql(
                            serverJob: serverJob, 
                            clientJob: clientJob,
                            connectionString: sqlConnectionString,
                            path: serverJob.Path,
                            session: session,
                            description: description,
                            dimension: "Latency99Percentile (ms)",
                            value: clientJob.Latency.Within99thPercentile.TotalMilliseconds);
                    }
                }
            }
            finally
            {
                if (serverJobUri != null)
                {
                    Log($"Stopping scenario {scenario} on benchmark server...");

                    LogVerbose($"DELETE {serverJobUri}...");
                    response = _httpClient.DeleteAsync(serverJobUri).Result;
                    LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");
                    response.EnsureSuccessStatusCode();
                }
            }

            return 0;
        }

        private static async Task<ClientJob> RunClientJob(string scenarioName, Uri clientUri, string serverBenchmarkUri)
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

        private static Task WriteJobsToSql(ServerJob serverJob, ClientJob clientJob, string connectionString, string path, string session, string description, string dimension, double value)
        {
            return WriteResultsToSql(
                        connectionString: connectionString,
                        scenario: serverJob.Scenario,
                        session: session,
                        description: description,
                        aspnetCoreVersion: serverJob.AspNetCoreVersion,
                        hardware: serverJob.Hardware.Value,
                        operatingSystem: serverJob.OperatingSystem.Value,
                        scheme: serverJob.Scheme,
                        sources: serverJob.ReferenceSources,
                        connectionFilter: serverJob.ConnectionFilter,
                        webHost: serverJob.WebHost,
                        kestrelThreadCount: serverJob.KestrelThreadCount,
                        kestrelThreadPoolDispatching: serverJob.KestrelThreadPoolDispatching,
                        kestrelTransport: serverJob.KestrelTransport,
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
            string scenario,
            Hardware hardware,
            OperatingSystem operatingSystem,
            Scheme scheme,
            IEnumerable<Source> sources,
            string connectionFilter,
            WebHost webHost,
            KestrelTransport? kestrelTransport,
            int? kestrelThreadCount,
            bool? kestrelThreadPoolDispatching,
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
                        [Scenario] [nvarchar](max) NOT NULL,
                        [Hardware] [nvarchar](max) NOT NULL,
                        [OperatingSystem] [nvarchar](max) NOT NULL,
                        [Framework] [nvarchar](max) NOT NULL,
                        [RuntimeStore] [bit] NULL,
                        [Scheme] [nvarchar](max) NOT NULL,
                        [Sources] [nvarchar](max) NULL,
                        [ConnectionFilter] [nvarchar](max) NULL,
                        [WebHost] [nvarchar](max) NOT NULL,
                        [KestrelTransport] [nvarchar](max) NULL,
                        [KestrelThreadCount] [int] NULL,
                        [KestrelThreadPoolDispatching] [bit] NULL,
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
                           ,[Scenario]
                           ,[Hardware]
                           ,[OperatingSystem]
                           ,[Framework]
                           ,[RuntimeStore]
                           ,[Scheme]
                           ,[Sources]
                           ,[ConnectionFilter]
                           ,[WebHost]
                           ,[KestrelTransport]
                           ,[KestrelThreadCount]
                           ,[KestrelThreadPoolDispatching]
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
                           ,@Scenario
                           ,@Hardware
                           ,@OperatingSystem
                           ,@Framework
                           ,@RuntimeStore
                           ,@Scheme
                           ,@Sources
                           ,@ConnectionFilter
                           ,@WebHost
                           ,@KestrelTransport
                           ,@KestrelThreadCount
                           ,@KestrelThreadPoolDispatching
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
                    p.AddWithValue("@Scenario", scenario.ToString());
                    p.AddWithValue("@Hardware", hardware.ToString());
                    p.AddWithValue("@OperatingSystem", operatingSystem.ToString());
                    p.AddWithValue("@Framework", "Core");
                    p.AddWithValue("@RuntimeStore", runtimeStore);
                    p.AddWithValue("@Scheme", scheme.ToString().ToLowerInvariant());
                    p.AddWithValue("@Sources", sources.Any() ? (object)ConvertToSqlString(sources) : DBNull.Value);
                    p.AddWithValue("@ConnectionFilter",
                        string.IsNullOrEmpty(connectionFilter) ? (object)DBNull.Value : connectionFilter);
                    p.AddWithValue("@WebHost", webHost.ToString());
                    p.AddWithValue("@KestrelTransport", kestrelTransport?.ToString() ?? (object)DBNull.Value);
                    p.AddWithValue("@KestrelThreadCount", (object)kestrelThreadCount ?? DBNull.Value);
                    p.AddWithValue("@KestrelThreadPoolDispatching", (object)kestrelThreadPoolDispatching ?? DBNull.Value);
                    p.AddWithValue("@ClientThreads", clientThreads);
                    p.AddWithValue("@Connections", connections);
                    p.AddWithValue("@Duration", duration);
                    p.AddWithValue("@PipelineDepth", (object)pipelineDepth ?? DBNull.Value);
                    p.AddWithValue("@Path", string.IsNullOrEmpty(path) ? (object)DBNull.Value : path);
                    p.AddWithValue("@Method", method.ToString().ToUpperInvariant());
                    p.AddWithValue("@Headers", headers.Any() ? JsonConvert.SerializeObject(headers) : null);
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
