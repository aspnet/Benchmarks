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

namespace BenchmarksDriver.Serializers
{
    class SignalRSerializer : IResultsSerializer
    {
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
                        [WebHost] [nvarchar](50) NOT NULL,
                        [Transport] [nvarchar](50) NOT NULL,
                        [HubProtocol] [nvarchar](50) NOT NULL,
                        [Connections] [int] NOT NULL,
                        [Duration] [int] NOT NULL,
                        [Path] [nvarchar](200) NULL,
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

        public async Task WriteJobResultsToSqlAsync(ServerJob serverJob, ClientJob clientJob, string connectionString, string tableName, string path, string session, string description, Statistics statistics, bool longRunning)
        {
            var utcNow = DateTime.UtcNow;

            await RetryOnExceptionAsync(5, async () =>
            {
                await WriteJobResultToSqlAsync(serverJob, clientJob, utcNow, connectionString, tableName, path, session, description, statistics, longRunning, "RequestsPerSecond", statistics.RequestsPerSecond);
            });

            await RetryOnExceptionAsync(5, async () =>
            {
                await WriteJobResultToSqlAsync(serverJob, clientJob, utcNow, connectionString, tableName, path, session, description, statistics, longRunning, "CPU", statistics.Cpu);
            });

            await RetryOnExceptionAsync(5, async () =>
            {
                await WriteJobResultToSqlAsync(serverJob, clientJob, utcNow, connectionString, tableName, path, session, description, statistics, longRunning, "WorkingSet (MB)", statistics.WorkingSet);
            });

            if (statistics.LatencyAverage != -1)
            {
                await RetryOnExceptionAsync(5, async () =>
                {
                    await WriteJobResultToSqlAsync(serverJob, clientJob, utcNow, connectionString, tableName, path, session, description, statistics, longRunning, "Latency Average (ms)", statistics.LatencyAverage);
                });
            }

            if (statistics.Latency50Percentile != -1)
            {
                await RetryOnExceptionAsync(5, async () =>
                {
                    await WriteJobResultToSqlAsync(serverJob, clientJob, utcNow, connectionString, tableName, path, session, description, statistics, longRunning, "Latency50Percentile (ms)", statistics.Latency50Percentile);
                });
            }

            if (statistics.Latency75Percentile != -1)
            {
                await RetryOnExceptionAsync(5, async () =>
                {
                    await WriteJobResultToSqlAsync(serverJob, clientJob, utcNow, connectionString, tableName, path, session, description, statistics, longRunning, "Latency75Percentile (ms)", statistics.Latency75Percentile);
                });
            }

            if (statistics.Latency90Percentile != -1)
            {
                await RetryOnExceptionAsync(5, async () =>
                {
                    await WriteJobResultToSqlAsync(serverJob, clientJob, utcNow, connectionString, tableName, path, session, description, statistics, longRunning, "Latency90Percentile (ms)", statistics.Latency90Percentile);
                });
            }

            if (statistics.Latency99Percentile != -1)
            {
                await RetryOnExceptionAsync(5, async () =>
                {
                    await WriteJobResultToSqlAsync(serverJob, clientJob, utcNow, connectionString, tableName, path, session, description, statistics, longRunning, "Latency99Percentile (ms)", statistics.Latency99Percentile);
                });
            }
        }

        private async Task WriteJobResultToSqlAsync(ServerJob serverJob, ClientJob clientJob, DateTime utcNow, string connectionString, string tableName, string path, string session, string description, Statistics statistics, bool longRunning, string dimension, double value)
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
                           ,[WebHost]
                           ,[Transport]
                           ,[HubProtocol]
                           ,[Connections]
                           ,[Duration]
                           ,[Path]
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
                           ,@WebHost
                           ,@Transport
                           ,@HubProtocol
                           ,@Connections
                           ,@Duration
                           ,@Path
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
                    p.AddWithValue("@DateTime", utcNow);
                    p.AddWithValue("@Session", session);
                    p.AddWithValue("@Description", description);
                    p.AddWithValue("@AspNetCoreVersion", serverJob.AspNetCoreVersion);
                    p.AddWithValue("@RuntimeVersion", serverJob.RuntimeVersion);
                    p.AddWithValue("@Scenario", serverJob.Scenario.ToString());
                    p.AddWithValue("@Hardware", serverJob.Hardware.ToString());
                    p.AddWithValue("@HardwareVersion", serverJob.HardwareVersion);
                    p.AddWithValue("@OperatingSystem", serverJob.OperatingSystem.ToString());
                    p.AddWithValue("@Framework", "Core");
                    p.AddWithValue("@RuntimeStore", serverJob.UseRuntimeStore);
                    p.AddWithValue("@Scheme", serverJob.Scheme.ToString().ToLowerInvariant());
                    p.AddWithValue("@Sources", serverJob.ReferenceSources.Any() ? (object)ConvertToSqlString(serverJob.ReferenceSources) : DBNull.Value);
                    p.AddWithValue("@WebHost", serverJob.WebHost.ToString());
                    p.AddWithValue("@Transport", clientJob.ClientProperties["TransportType"]);
                    p.AddWithValue("@HubProtocol", clientJob.ClientProperties["HubProtocol"]);
                    p.AddWithValue("@Connections", clientJob.Connections);
                    p.AddWithValue("@Duration", clientJob.Duration);
                    p.AddWithValue("@Path", string.IsNullOrEmpty(path) ? (object)DBNull.Value : path);
                    p.AddWithValue("@Headers", clientJob.Headers.Any() ? JsonConvert.SerializeObject(clientJob.Headers) : (object)DBNull.Value);
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

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }

        public void ComputeAverages(Statistics average, IEnumerable<Statistics> samples)
        {
            // TODO: Do we want to do anything custom here
        }

        public void Dispose()
        {
        }
    }
}
