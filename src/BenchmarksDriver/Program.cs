// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Benchmarks.ServerJob;
using Microsoft.Extensions.CommandLineUtils;
using Newtonsoft.Json;

using OperatingSystem = Benchmarks.ServerJob.OperatingSystem;

namespace BenchmarksDriver
{
    public class Program
    {
        private static bool _verbose;

        private static readonly HttpClient _httpClient = new HttpClient();

        private static readonly ClientJob _baseJob = new ClientJob()
        {
            Connections = 256,
            Threads = 32,
            Duration = 15,
        };

        private static readonly ClientJob _plaintextJob = new ClientJob(_baseJob)
        {
            Headers = new string[] {
                "Host: localhost",
                "Accept: text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7",
                "Connection: keep-alive"
            },
            ScriptName = "pipeline",
            PipelineDepth = 16,
        };

        private static readonly ClientJob _jsonJob = new ClientJob(_baseJob)
        {
            Headers = new string[] {
                "Host: localhost",
                "Accept: application/json,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7",
                "Connection: keep-alive"
            },
        };

        private static readonly ClientJob _htmlJob = new ClientJob(_baseJob)
        {
            Headers = new string[] {
                "Host: localhost",
                "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                "Connection: keep-alive"
            },
        };

        private static readonly ClientJob _multiQueryJob = new ClientJob(_jsonJob)
        {
            Query = "?queries=20",
        };

        private static readonly Dictionary<Scenario, ClientJob> _clientJobs =
            new Dictionary<Scenario, ClientJob>()
            {
                { Scenario.Plaintext, _plaintextJob },
                { Scenario.Json, _jsonJob },
                { Scenario.Jil, _jsonJob },
                { Scenario.CopyToAsync, new ClientJob(_plaintextJob) {
                    ScriptName = "post",
                } },
                { Scenario.MvcPlaintext, _plaintextJob },
                { Scenario.MvcJson, _jsonJob },
                { Scenario.MvcJil, _jsonJob },
                { Scenario.MemoryCachePlaintext, _plaintextJob },
                { Scenario.MemoryCachePlaintextSetRemove, _plaintextJob },
                { Scenario.ResponseCachingPlaintextCached, _plaintextJob },
                { Scenario.ResponseCachingPlaintextResponseNoCache, _plaintextJob },
                { Scenario.ResponseCachingPlaintextRequestNoCache, new ClientJob(_plaintextJob) {
                    Headers = _plaintextJob.Headers.Append("Cache-Control: no-cache")
                } },
                { Scenario.ResponseCachingPlaintextVaryByCached, _plaintextJob },
                { Scenario.StaticFiles, _plaintextJob },
                { Scenario.DbSingleQueryRaw, _jsonJob },
                { Scenario.DbSingleQueryDapper, _jsonJob },
                { Scenario.DbSingleQueryEf, _jsonJob },
                { Scenario.MvcDbSingleQueryRaw, _jsonJob },
                { Scenario.MvcDbSingleQueryDapper, _jsonJob },
                { Scenario.MvcDbSingleQueryEf, _jsonJob },
                { Scenario.DbMultiQueryRaw, _multiQueryJob },
                { Scenario.DbMultiQueryDapper, _multiQueryJob },
                { Scenario.DbMultiQueryEf, _multiQueryJob },
                { Scenario.MvcDbMultiQueryRaw, _multiQueryJob },
                { Scenario.MvcDbMultiQueryDapper, _multiQueryJob },
                { Scenario.MvcDbMultiQueryEf, _multiQueryJob },
                { Scenario.DbMultiUpdateRaw, _multiQueryJob },
                { Scenario.DbMultiUpdateDapper, _multiQueryJob },
                { Scenario.DbMultiUpdateEf, _multiQueryJob },
                { Scenario.MvcDbMultiUpdateRaw, _multiQueryJob },
                { Scenario.MvcDbMultiUpdateDapper, _multiQueryJob },
                { Scenario.MvcDbMultiUpdateEf, _multiQueryJob },
                { Scenario.DbFortunesRaw, _htmlJob },
                { Scenario.DbFortunesDapper, _htmlJob },
                { Scenario.DbFortunesEf, _htmlJob },
                { Scenario.MvcDbFortunesRaw, _htmlJob },
                { Scenario.MvcDbFortunesDapper, _htmlJob },
                { Scenario.MvcDbFortunesEf, _htmlJob },
            };

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

            // ClientJob Options
            var clientThreadsOption = app.Option("--clientThreads",
                "Number of threads used by client", CommandOptionType.SingleValue);
            var connectionsOption = app.Option("--connections",
                "Number of connections used by client", CommandOptionType.SingleValue);
            var durationOption = app.Option("--duration",
                "Duration of test in seconds", CommandOptionType.SingleValue);
            var headerOption = app.Option("--header",
                "Header added to request", CommandOptionType.MultipleValue);
            var methodOption = app.Option("--method",
                "HTTP method of the request. Default is GET.", CommandOptionType.SingleValue);
            var pipelineDepthOption = app.Option("--pipelineDepth",
                "Depth of pipeline used by client", CommandOptionType.SingleValue);
            var pathOption = app.Option(
                "--path",
                "Relative URL where the client should send requests.",
                CommandOptionType.SingleValue);

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

                var server = serverOption.Value();
                var client = clientOption.Value();
                var sqlConnectionString = sqlConnectionStringOption.Value();

                if (!Enum.TryParse(schemeValue, ignoreCase: true, result: out Scheme scheme) ||
                    !Enum.TryParse(scenarioOption.Value(), ignoreCase: true, result: out Scenario scenario) ||
                    !Enum.TryParse(webHostValue, ignoreCase: true, result: out WebHost webHost) ||
                    string.IsNullOrWhiteSpace(server) ||
                    string.IsNullOrWhiteSpace(client))
                {
                    app.ShowHelp();
                    return 2;
                }

                string path = null;
                var scenarioName = scenario.ToString();
                var field = typeof(Scenario).GetTypeInfo().GetField(scenarioName);
                var pathAttribute = field.GetCustomAttribute<ScenarioPathAttribute>();
                if (pathAttribute != null)
                {
                    Debug.Assert(pathAttribute.Paths.Length > 0);
                    if (pathAttribute.Paths.Length == 1)
                    {
                        if (pathOption.HasValue())
                        {
                            Console.WriteLine($"Scenario '{scenarioName}' does not support the {pathOption.LongName} option.");
                            return 4;
                        }
                    }
                    else
                    {
                        if (!pathOption.HasValue())
                        {
                            Console.WriteLine($"Scenario '{scenarioName}' requires one of the following {pathOption.LongName} options:");
                            Console.WriteLine($"'{string.Join("', '", pathAttribute.Paths)}'");
                            return 5;
                        }

                        path = pathOption.Value();

                        if (!pathAttribute.Paths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) &&
                            !pathAttribute.Paths.Any(p => string.Equals(p, "/" + path, StringComparison.OrdinalIgnoreCase)))
                        {
                            Console.WriteLine($"Scenario '{scenarioName}' does not support {pathOption.LongName} '{pathOption.Value()}'. Choose from:");
                            Console.WriteLine($"'{string.Join("', '", pathAttribute.Paths)}'");
                            return 6;
                        }
                    }
                }

                var serverJob = new ServerJob()
                {
                    Scheme = scheme,
                    Scenario = scenario,
                    WebHost = webHost
                };

                if (connectionFilterOption.HasValue())
                {
                    serverJob.ConnectionFilter = connectionFilterOption.Value();
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

                var sources = new List<Source>();
                foreach (var source in sourceOption.Values)
                {
                    var split = source.IndexOf('@');
                    var repository = (split == -1) ? source : source.Substring(0, split);
                    var branch = (split == -1) ? null : source.Substring(split + 1);

                    if (!repository.Contains(":"))
                    {
                        repository = $"https://github.com/aspnet/{repository}.git";
                    }

                    sources.Add(new Source() { BranchOrCommit = branch, Repository = repository });
                }
                serverJob.Sources = sources;

                // Override default ClientJob settings if options are set
                if (connectionsOption.HasValue())
                {
                    _clientJobs.Values.ToList().ForEach(c => c.Connections = int.Parse(connectionsOption.Value()));
                }
                if (clientThreadsOption.HasValue())
                {
                    _clientJobs.Values.ToList().ForEach(c => c.Threads = int.Parse(clientThreadsOption.Value()));
                }
                if (durationOption.HasValue())
                {
                    _clientJobs.Values.ToList().ForEach(c => c.Duration = int.Parse(durationOption.Value()));
                }
                if (pipelineDepthOption.HasValue())
                {
                    _clientJobs.Values.ToList().ForEach(c => c.PipelineDepth = int.Parse(pipelineDepthOption.Value()));
                }
                if (methodOption.HasValue())
                {
                    _clientJobs.Values.ToList().ForEach(c => c.Method = methodOption.Value());
                }
                if (headerOption.HasValue())
                {
                    _clientJobs.Values.ToList().ForEach(c => c.Headers = headerOption.Values.ToArray());
                }

                return Run(new Uri(server), new Uri(client), sqlConnectionString, serverJob, path).Result;
            });

            return app.Execute(args);
        }

        private static async Task<int> Run(
            Uri serverUri,
            Uri clientUri,
            string sqlConnectionString,
            ServerJob serverJob,
            string path)
        {
            var scenario = serverJob.Scenario;
            var serverJobsUri = new Uri(serverUri, "/jobs");
            Uri serverJobUri = null;
            HttpResponseMessage response = null;
            string responseContent = null;

            try
            {
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

                if (path != null)
                {
                    serverBenchmarkUri += path.Trim('/');
                }

                Log("Warmup");
                await RunClientJob(scenario, clientUri, serverBenchmarkUri);

                Log("Benchmark");
                var clientJob = await RunClientJob(scenario, clientUri, serverBenchmarkUri);

                if (clientJob.State == ClientState.Completed && !string.IsNullOrWhiteSpace(sqlConnectionString))
                {
                    await WriteResultsToSql(
                        connectionString: sqlConnectionString,
                        scenario: scenario,
                        hardware: serverJob.Hardware.Value,
                        operatingSystem: serverJob.OperatingSystem.Value,
                        scheme: serverJob.Scheme,
                        sources: serverJob.Sources,
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
                        rps: clientJob.RequestsPerSecond);
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

        private static async Task<ClientJob> RunClientJob(Scenario scenario, Uri clientUri, string serverBenchmarkUri)
        {
            var clientJob = new ClientJob(_clientJobs[scenario]) { ServerBenchmarkUri = serverBenchmarkUri };

            Uri clientJobUri = null;
            try
            {
                Log($"Starting scenario {scenario} on benchmark client...");

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
                        Log($"Scenario {scenario} completed on benchmark client");
                        LogVerbose($"Output: {clientJob.Output}");
                        LogVerbose($"Error: {clientJob.Error}");
                        Log($"RPS: {clientJob.RequestsPerSecond}");
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
                    Log($"Stopping scenario {scenario} on benchmark client...");

                    LogVerbose($"DELETE {clientJobUri}...");
                    var response = _httpClient.DeleteAsync(clientJobUri).Result;
                    LogVerbose($"{(int)response.StatusCode} {response.StatusCode}");
                    response.EnsureSuccessStatusCode();
                }
            }

            return clientJob;
        }

        private static async Task WriteResultsToSql(
            string connectionString,
            Scenario scenario,
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
            IEnumerable<string> headers,
            double rps)
        {
            Log("Writing results to SQL...");

            const string createCmd =
                @"
                IF OBJECT_ID(N'dbo.AspNetBenchmarks', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[AspNetBenchmarks](
                        [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [DateTime] [datetimeoffset](7) NOT NULL,
                        [Scenario] [nvarchar](max) NOT NULL,
                        [Hardware] [nvarchar](max) NOT NULL,
                        [OperatingSystem] [nvarchar](max) NOT NULL,
                        [Framework] [nvarchar](max) NOT NULL,
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
                        [RequestsPerSecond] [float] NOT NULL
                    )
                END
                ";

            const string insertCmd =
                @"
                INSERT INTO [dbo].[AspNetBenchmarks]
                           ([DateTime]
                           ,[Scenario]
                           ,[Hardware]
                           ,[OperatingSystem]
                           ,[Framework]
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
                           ,[RequestsPerSecond])
                     VALUES
                           (@DateTime
                           ,@Scenario
                           ,@Hardware
                           ,@OperatingSystem
                           ,@Framework
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
                           ,@RequestsPerSecond)
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
                    p.AddWithValue("@Scenario", scenario.ToString());
                    p.AddWithValue("@Hardware", hardware.ToString());
                    p.AddWithValue("@OperatingSystem", operatingSystem.ToString());
                    p.AddWithValue("@Framework", "Core");
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
                    p.AddWithValue("@Headers", headers.Any() ? (object)headers.ToContentString() : DBNull.Value);
                    p.AddWithValue("@RequestsPerSecond", rps);

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
