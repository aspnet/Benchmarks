// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Benchmarks.ServerJob;
using Newtonsoft.Json;

namespace BenchmarksWorkers.Workers
{
    public class WrkSerializer : IResultsSerializer
    {
        public string JobLogText { get; set; }

        static WrkSerializer()
        {
            // Register the serializer
            WorkerFactory.ResultSerializers["wrk"] = () => new WrkSerializer();
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


        public void Dispose()
        {
        }
    }
}