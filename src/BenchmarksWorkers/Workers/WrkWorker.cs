// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Benchmarks.ServerJob;
using Newtonsoft.Json;

namespace BenchmarksWorkers.Workers
{
    public class WrkWorker : IWorker
    {
        private static HttpClient _httpClient;
        private static HttpClientHandler _httpClientHandler;

        private ClientJob _job;
        private Process _process;

        public string JobLogText { get; set; }

        static WrkWorker()
        {
            // Register the worker
            WorkerFactory.Workers["wrk"] = clientJob => new WrkWorker(clientJob);

            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClient = new HttpClient(_httpClientHandler);
        }

        public WrkWorker(ClientJob clientJob)
        {
            _job = clientJob;            

            _job.ClientProperties.TryGetValue("ScriptName", out var scriptName);

            if (_job.ClientProperties.TryGetValue("PipelineDepth", out var pipelineDepth))
            {
                Debug.Assert(int.Parse(pipelineDepth) <= 0 || scriptName != null, "A script name must be present when the pipeline depth is larger than 0.");
            }

            var jobLogText =
                        $"[ID:{_job.Id} Connections:{_job.Connections} Threads:{_job.Threads} Duration:{_job.Duration} Method:{_job.Method} ServerUrl:{_job.ServerBenchmarkUri}";

            if (!string.IsNullOrEmpty(scriptName))
            {
                jobLogText += $" Script:{scriptName}";
            }

            if (pipelineDepth != null && int.Parse(pipelineDepth) > 0)
            {
                jobLogText += $" Pipeline:{pipelineDepth}";
            }

            if (_job.Headers != null)
            {
                jobLogText += $" Headers:{JsonConvert.SerializeObject(_job.Headers)}";
            }

            jobLogText += "]";

            JobLogText = jobLogText;
        }

        public async Task StartAsync()
        {
            await MeasureFirstRequestLatencyAsync(_job);

            _job.State = ClientState.Running;
            _job.LastDriverCommunicationUtc = DateTime.UtcNow;

            _process = StartProcess(_job);
        }

        public Task StopAsync()
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _process.Dispose();
            _process = null;
        }
        private static HttpRequestMessage CreateHttpMessage(ClientJob job)
        {
            var requestMessage = new HttpRequestMessage(new HttpMethod(job.Method), job.ServerBenchmarkUri);

            foreach (var header in job.Headers)
            {
                requestMessage.Headers.Add(header.Key, header.Value);
            }

            return requestMessage;
        }

        private static async Task MeasureFirstRequestLatencyAsync(ClientJob job)
        {
            if (job.SkipStartupLatencies)
            {
                return;
            }

            Log($"Measuring first request latency on {job.ServerBenchmarkUri}");

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            using (var response = await _httpClient.SendAsync(CreateHttpMessage(job)))
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                job.LatencyFirstRequest = stopwatch.Elapsed;
            }

            Log($"{job.LatencyFirstRequest.TotalMilliseconds} ms");

            Log("Measuring subsequent requests latency");

            for (var i = 0; i < 10; i++)
            {
                stopwatch.Restart();

                using (var response = await _httpClient.SendAsync(CreateHttpMessage(job)))
                {
                    var responseContent = await response.Content.ReadAsStringAsync();

                    // We keep the last measure to simulate a warmup phase.
                    job.LatencyNoLoad = stopwatch.Elapsed;
                }
            }

            Log($"{job.LatencyNoLoad.TotalMilliseconds} ms");
        }

        private static Process StartProcess(ClientJob job)
        {
            var command = "wrk";

            if (job.Headers != null)
            {
                foreach (var header in job.Headers)
                {
                    command += $" -H \"{header.Key}: {header.Value}\"";
                }
            }

            command += $" --latency -d {job.Duration} -c {job.Connections} --timeout 8 -t {job.Threads}  {job.ServerBenchmarkUri}{job.Query}";

            if (job.ClientProperties.TryGetValue("ScriptName", out var scriptName) && !string.IsNullOrEmpty(scriptName))
            {
                command += $" -s scripts/{scriptName}.lua --";

                var pipeLineDepth = int.Parse(job.ClientProperties["PipelineDepth"]);
                if (pipeLineDepth > 0)
                {
                    command += $" {pipeLineDepth}";
                }

                if (job.Method != "GET")
                {
                    command += $" {job.Method}";
                }
            }

            Log(command);

            var process = new Process()
            {
                StartInfo = {
                    FileName = "stdbuf",
                    Arguments = $"-oL {command}",
                    WorkingDirectory = Path.GetDirectoryName(typeof(WrkWorker).GetTypeInfo().Assembly.Location),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    Log(e.Data);
                    job.Output += (e.Data + Environment.NewLine);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    Log(e.Data);
                    job.Error += (e.Data + Environment.NewLine);
                }
            };

            process.Exited += (_, __) =>
            {
                // Wait for all Output messages to be flushed and available in job.Output
                Thread.Sleep(100);

                var rpsMatch = Regex.Match(job.Output, @"Requests/sec:\s*([\d\.]*)");
                if (rpsMatch.Success && rpsMatch.Groups.Count == 2)
                {
                    job.RequestsPerSecond = double.Parse(rpsMatch.Groups[1].Value);
                }

                const string LatencyPattern = @"\s+{0}\s+([\d\.]+)(\w+)";

                var latencyMatch = Regex.Match(job.Output, String.Format(LatencyPattern, "Latency"));
                job.Latency.Average = ReadLatency(latencyMatch);

                var p50Match = Regex.Match(job.Output, String.Format(LatencyPattern, "50%"));
                job.Latency.Within50thPercentile = ReadLatency(p50Match);

                var p75Match = Regex.Match(job.Output, String.Format(LatencyPattern, "75%"));
                job.Latency.Within75thPercentile = ReadLatency(p75Match);

                var p90Match = Regex.Match(job.Output, String.Format(LatencyPattern, "90%"));
                job.Latency.Within90thPercentile = ReadLatency(p90Match);

                var p99Match = Regex.Match(job.Output, String.Format(LatencyPattern, "99%"));
                job.Latency.Within99thPercentile = ReadLatency(p99Match);

                var socketErrorsMatch = Regex.Match(job.Output, @"Socket errors: connect ([\d\.]*), read ([\d\.]*), write ([\d\.]*), timeout ([\d\.]*)");
                job.SocketErrors = CountSocketErrors(socketErrorsMatch);

                var badResponsesMatch = Regex.Match(job.Output, @"Non-2xx or 3xx responses: ([\d\.]*)");
                job.BadResponses = ReadBadReponses(badResponsesMatch);

                var requestsCountMatch = Regex.Match(job.Output, @"([\d\.]*) requests in ([\d\.]*)(\w*)");
                job.Requests = ReadRequests(requestsCountMatch);
                job.ActualDuration = ReadDuration(requestsCountMatch);

                job.State = ClientState.Completed;
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        private static TimeSpan ReadDuration(Match responseCountMatch)
        {
            if (!responseCountMatch.Success || responseCountMatch.Groups.Count != 4)
            {
                Log("Failed to parse duration");
                return TimeSpan.Zero;
            }

            try
            {
                var value = double.Parse(responseCountMatch.Groups[2].Value);

                var unit = responseCountMatch.Groups[3].Value;

                switch (unit)
                {
                    case "s": return TimeSpan.FromSeconds(value);
                    case "m": return TimeSpan.FromMinutes(value);
                    case "h": return TimeSpan.FromHours(value);

                    default: throw new NotSupportedException("Failed to parse duration unit: " + unit);
                }
            }
            catch
            {
                Log("Failed to parse durations");
                return TimeSpan.Zero;
            }
        }

        private static int ReadRequests(Match responseCountMatch)
        {
            if (!responseCountMatch.Success || responseCountMatch.Groups.Count != 4)
            {
                Log("Failed to parse requests");
                return -1;
            }

            try
            {
                return int.Parse(responseCountMatch.Groups[1].Value);
            }
            catch
            {
                Log("Failed to parse requests");
                return -1;
            }
        }

        private static int ReadBadReponses(Match badResponsesMatch)
        {
            if (!badResponsesMatch.Success || badResponsesMatch.Groups.Count != 2)
            {
                Log("Failed to parse bad responses");
                return 0;
            }

            try
            {
                return int.Parse(badResponsesMatch.Groups[1].Value);
            }
            catch
            {
                Log("Failed to parse bad responses");
                return 0;
            }
        }

        private static int CountSocketErrors(Match socketErrorsMatch)
        {
            if (!socketErrorsMatch.Success || socketErrorsMatch.Groups.Count != 5)
            {
                Log("Failed to parse bad responses");
                return 0;
            }

            try
            {
                return
                    int.Parse(socketErrorsMatch.Groups[1].Value) +
                    int.Parse(socketErrorsMatch.Groups[2].Value) +
                    int.Parse(socketErrorsMatch.Groups[3].Value) +
                    int.Parse(socketErrorsMatch.Groups[4].Value)
                    ;

            }
            catch
            {
                Log("Failed to parse socket errors");
                return 0;
            }

        }

        private static double ReadLatency(Match match)
        {
            if (!match.Success || match.Groups.Count != 3)
            {
                Log("Failed to parse latency");
                return -1;
            }

            try
            {
                var value = double.Parse(match.Groups[1].Value);
                var unit = match.Groups[2].Value;

                switch (unit)
                {
                    case "s": return value * 1000;
                    case "ms": return value;
                    case "us": return value / 1000;

                    default:
                        Log("Failed to parse latency unit: " + unit);
                        return -1;
                }
            }
            catch
            {
                Log("Failed to parse latency");
                return -1;
            }
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }

        public async Task WriteJobResultsToSqlAsync(
            ServerJob serverJob, 
            ClientJob clientJob, 
            string sqlConnectionString, 
            string tableName,
            string path, 
            string session, 
            string description, 
            Statistics statistics,
            bool longRunning)
        {
            await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "RequestsPerSecond",
                value: statistics.RequestsPerSecond);

            if (statistics.StartupMain != -1 && !longRunning)
            {
                await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "Startup Main (ms)",
                value: statistics.StartupMain);
            }

            if (statistics.FirstRequest != -1 && !longRunning)
            {
                await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "First Request (ms)",
                value: statistics.FirstRequest);
            }

            await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "WorkingSet (MB)",
                value: statistics.WorkingSet);

            await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "CPU",
                value: statistics.Cpu);

            if (statistics.Latency != -1 && !longRunning)
            {
                await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                session: session,
                description: description,
                path: serverJob.Path,
                dimension: "Latency (ms)",
                value: statistics.Latency);
            }

            if (statistics.LatencyAverage != -1)
            {
                await WriteJobsToSql(
                    serverJob: serverJob,
                    clientJob: clientJob,
                    connectionString: sqlConnectionString,
                    tableName: tableName,
                    path: serverJob.Path,
                    session: session,
                    description: description,
                    dimension: "LatencyAverage (ms)",
                    value: statistics.LatencyAverage);
            }

            if (statistics.Latency50Percentile != -1)
            {
                await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "Latency50Percentile (ms)",
                value: statistics.Latency50Percentile);
            }

            if (statistics.Latency75Percentile != -1)
            {
                await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "Latency75Percentile (ms)",
                value: statistics.Latency75Percentile);
            }

            if (statistics.Latency90Percentile != -1)
            {
                await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "Latency90Percentile (ms)",
                value: statistics.Latency90Percentile);
            }

            if (statistics.Latency99Percentile != -1)
            {
                await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "Latency99Percentile (ms)",
                value: statistics.Latency99Percentile);
            }

            if (statistics.SocketErrors != -1)
            {
                await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "SocketErrors",
                value: statistics.SocketErrors);
            }

            if (statistics.BadResponses != -1)
            {
                await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "BadResponses",
                value: statistics.BadResponses);
            }

            if (statistics.TotalRequests != -1)
            {
                await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "TotalRequests",
                value: statistics.TotalRequests);
            }

            if (statistics.Duration != -1)
            {
                await WriteJobsToSql(
                serverJob: serverJob,
                clientJob: clientJob,
                connectionString: sqlConnectionString,
                tableName: tableName,
                path: serverJob.Path,
                session: session,
                description: description,
                dimension: "Duration (ms)",
                value: statistics.Duration);
            }
        }

        private Task WriteJobsToSql(ServerJob serverJob, ClientJob clientJob, string connectionString, string tableName, string path, string session, string description, string dimension, double value)
        {
            return RetryOnExceptionAsync(5, () =>
                 WriteResultsToSql(
                        connectionString: connectionString,
                        tableName: tableName,
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
                        runtimeStore: serverJob.UseRuntimeStore)
            , 5000);
        }

        public async Task InitializeDatabaseAsync(string connectionString, string tableName)
        {
            string createCmd =
                @"
                IF OBJECT_ID(N'dbo." + tableName + @"', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[" + tableName + @"](
                        [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [Excluded] [bit] DEFAULT 0,
                        [DateTime] [datetimeoffset](7) NOT NULL,
                        [Session] [nvarchar](200) NOT NULL,
                        [Description] [nvarchar](200),
                        [AspNetCoreVersion] [nvarchar](50) NOT NULL,
                        [RuntimeVersion] [nvarchar](50) NOT NULL,
                        [Scenario] [nvarchar](50) NOT NULL,
                        [Hardware] [nvarchar](50) NOT NULL,
                        [HardwareVersion] [nvarchar](128) NOT NULL,
                        [OperatingSystem] [nvarchar](50) NOT NULL,
                        [Framework] [nvarchar](50) NOT NULL,
                        [RuntimeStore] [bit] NOT NULL,
                        [Scheme] [nvarchar](50) NOT NULL,
                        [Sources] [nvarchar](50) NULL,
                        [ConnectionFilter] [nvarchar](50) NULL,
                        [WebHost] [nvarchar](50) NOT NULL,
                        [KestrelThreadCount] [int] NULL,
                        [ClientThreads] [int] NOT NULL,
                        [Connections] [int] NOT NULL,
                        [Duration] [int] NOT NULL,
                        [PipelineDepth] [int] NULL,
                        [Path] [nvarchar](200) NULL,
                        [Method] [nvarchar](50) NOT NULL,
                        [Headers] [nvarchar](max) NULL,
                        [Dimension] [nvarchar](50) NOT NULL,
                        [Value] [float] NOT NULL
                    )
                END
                ";

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqlCommand(createCmd, connection))
                {
                    await command.ExecuteNonQueryAsync();
                }
            }

        }

        private async Task WriteResultsToSql(
            string connectionString,
            string tableName,
            string session,
            string description,
            string aspnetCoreVersion,
            string runtimeVersion,
            string scenario,
            Hardware hardware,
            string hardwareVersion,
            Benchmarks.ServerJob.OperatingSystem operatingSystem,
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

            string insertCmd =
                @"
                INSERT INTO [dbo].[" + tableName + @"]
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