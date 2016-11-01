// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Benchmarks.ServerJob;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.Extensions.CommandLineUtils;
using Newtonsoft.Json;

namespace BenchmarkDriver
{
    public class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        private static readonly Dictionary<Scenario, ClientJob> _clientJobs =
            new Dictionary<Scenario, ClientJob>()
            {
                { Scenario.Plaintext, new ClientJob() {
                    Connections = 256, Threads = 32, Duration = 10, PipelineDepth = 16
                } },
                { Scenario.Json, new ClientJob() {
                    Connections = 256, Threads = 32, Duration = 10
                } },
                { Scenario.MvcPlaintext, new ClientJob() {
                    Connections = 256, Threads = 32, Duration = 10, PipelineDepth = 16
                } },
                { Scenario.MvcJson, new ClientJob() {
                    Connections = 256, Threads = 32, Duration = 10
                } },
                { Scenario.MemoryCachePlaintext, new ClientJob() {
                    Connections = 256, Threads = 32, Duration = 10, PipelineDepth = 16
                } },
                { Scenario.MemoryCachePlaintextSetRemove, new ClientJob() {
                    Connections = 256, Threads = 32, Duration = 10, PipelineDepth = 16
                } },
                { Scenario.ResponseCachingPlaintextCached, new ClientJob() {
                    Connections = 256, Threads = 32, Duration = 10, PipelineDepth = 16
                } },
                { Scenario.ResponseCachingPlaintextResponseNoCache, new ClientJob() {
                    Connections = 256, Threads = 32, Duration = 10, PipelineDepth = 16
                } },
                { Scenario.ResponseCachingPlaintextRequestNoCache, new ClientJob() {
                    Connections = 256, Threads = 32, Duration = 10, PipelineDepth = 16,
                    Headers = new string[] { "Cache-Control: no-cache" }
                } },
                { Scenario.ResponseCachingPlaintextVaryByCached, new ClientJob() {
                    Connections = 256, Threads = 32, Duration = 10, PipelineDepth = 16,
                    Headers = new string[] { "Accept: text/plain" }
                } },
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
            var serverOption = app.Option("-s|--server",
                "URL of benchmark server", CommandOptionType.SingleValue);
            var clientOption = app.Option("-c|--client",
                "URL of benchmark client", CommandOptionType.SingleValue);
            var sqlConnectionStringOption = app.Option("-q|--sql",
                "Connection string of SQL Database to store results", CommandOptionType.SingleValue);

            // ServerJob Options
            var connectionFilterOption = app.Option("-f|--connectionFilter",
                "Assembly-qualified name of the ConnectionFilter", CommandOptionType.SingleValue);
            var scenarioOption = app.Option("-n|--scenario",
                "Benchmark scenario to run", CommandOptionType.SingleValue);
            var schemeOption = app.Option("-m|--scheme",
                "Scheme (http or https).  Default is http.", CommandOptionType.SingleValue);
            var sourceOption = app.Option("-o|--source",
                "Source dependency. Format is 'repo@branchOrCommit'. " +
                "Repo can be a full URL, or a short name under https://github.com/aspnet.",
                CommandOptionType.MultipleValue);

            // ClientJob Options
            var connectionsOption = app.Option("--connections",
                "Number of connections used by client", CommandOptionType.SingleValue);
            var durationOption = app.Option("--duration",
                "Duration of test in seconds", CommandOptionType.SingleValue);
            var pipelineDepthOption = app.Option("--pipelineDepth",
                "Depth of pipeline used by client", CommandOptionType.SingleValue);
            var threadsOption = app.Option("--threads",
                "Number of threads used by client", CommandOptionType.SingleValue);
            var headerOption = app.Option("--header",
                "Header added to request", CommandOptionType.MultipleValue);

            app.OnExecute(() =>
            {
                var schemeValue = schemeOption.Value();
                if (string.IsNullOrEmpty(schemeValue))
                {
                    schemeValue = "http";
                }

                var server = serverOption.Value();
                var client = clientOption.Value();
                var sqlConnectionString = sqlConnectionStringOption.Value();

                Scheme scheme;
                Scenario scenario;
                if (!Enum.TryParse(schemeValue, ignoreCase: true, result: out scheme) ||
                    !Enum.TryParse(scenarioOption.Value(), ignoreCase: true, result: out scenario) ||
                    string.IsNullOrWhiteSpace(server) ||
                    string.IsNullOrWhiteSpace(client))
                {
                    app.ShowHelp();
                    return 2;
                }

                var serverJob = new ServerJob()
                {
                    Scheme = scheme,
                    Scenario = scenario,
                };

                if (connectionFilterOption.HasValue())
                {
                    serverJob.ConnectionFilter = connectionFilterOption.Value();
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
                if (threadsOption.HasValue())
                {
                    _clientJobs.Values.ToList().ForEach(c => c.Threads = int.Parse(threadsOption.Value()));
                }
                if (durationOption.HasValue())
                {
                    _clientJobs.Values.ToList().ForEach(c => c.Duration = int.Parse(durationOption.Value()));
                }
                if (pipelineDepthOption.HasValue())
                {
                    _clientJobs.Values.ToList().ForEach(c => c.PipelineDepth = int.Parse(pipelineDepthOption.Value()));
                }
                if (headerOption.HasValue())
                {
                    _clientJobs.Values.ToList().ForEach(c => c.Headers = headerOption.Values.ToArray());
                }

                return Run(new Uri(server), new Uri(client), sqlConnectionString, serverJob).Result;
            });

            return app.Execute(args);
        }

        private static async Task<int> Run(Uri serverUri, Uri clientUri, string sqlConnectionString, ServerJob serverJob)
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

                    if (serverJob.State == ServerState.Running)
                    {
                        serverBenchmarkUri = serverJob.Url;
                        break;
                    }
                    else if (serverJob.State == ServerState.Failed)
                    {
                        throw new InvalidOperationException("Server job failed");
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

                if (clientJob.State == ClientState.Completed && !string.IsNullOrWhiteSpace(sqlConnectionString))
                {
                    await WriteResultsToSql(sqlConnectionString, scenario, serverJob.Scheme, serverJob.ConnectionFilter, clientJob.Threads,
                        clientJob.Connections, clientJob.Duration, clientJob.PipelineDepth, clientJob.Headers, clientJob.RequestsPerSecond);
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

                    if (clientJob.State == ClientState.Running)
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
            Scheme scheme,
            string connectionFilter,
            int threads,
            int connections,
            int duration,
            int? pipelineDepth,
            string[] headers,
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
                        [Scheme] [nvarchar](max) NOT NULL,
                        [ConnectionFilter] [nvarchar](max) NULL,
                        [Threads] [int] NOT NULL,
                        [Connections] [int] NOT NULL,
                        [Duration] [int] NOT NULL,
                        [PipelineDepth] [int] NULL,
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
                           ,[Scheme]
                           ,[ConnectionFilter]
                           ,[Threads]
                           ,[Connections]
                           ,[Duration]
                           ,[PipelineDepth]
                           ,[Headers]
                           ,[RequestsPerSecond])
                     VALUES
                           (@DateTime
                           ,@Scenario
                           ,@Scheme
                           ,@ConnectionFilter
                           ,@Threads
                           ,@Connections
                           ,@Duration
                           ,@PipelineDepth
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
                    p.AddWithValue("@Scheme", scheme.ToString().ToLowerInvariant());
                    p.AddWithValue("@ConnectionFilter",
                        string.IsNullOrEmpty(connectionFilter) ? (object)DBNull.Value : connectionFilter);
                    p.AddWithValue("@Threads", threads);
                    p.AddWithValue("@Connections", connections);
                    p.AddWithValue("@Duration", duration);
                    p.AddWithValue("@PipelineDepth", (object)pipelineDepth ?? DBNull.Value);
                    p.AddWithValue("@Headers", headers == null ? (object)DBNull.Value : headers.ToContentString());
                    p.AddWithValue("@RequestsPerSecond", rps);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private static void Log(string message)
        {
            Log(message, Reporter.Output);
        }

        private static void LogVerbose(string message)
        {
            Log(message, Reporter.Verbose);
        }

        private static void Log(string message, Reporter reporter)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            reporter.WriteLine($"[{time}] {message}");
        }
    }
}