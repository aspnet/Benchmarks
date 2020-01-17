// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.SqlClient;
using System.Text.Json;
using System.Threading.Tasks;

namespace BenchmarksDriver.Serializers
{
    public class JobSerializer
    {
        private static JsonSerializerOptions _serializerOptions = new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static Task WriteJobResultsToSqlAsync(
            JobResults jobResults, 
            string sqlConnectionString, 
            string tableName,
            string session,
            string category,
            string scenario,
            string hardware,
            string architecture,
            string operatingSystem)
        {
            var utcNow = DateTime.UtcNow;

            var document = JsonSerializer.Serialize(jobResults, _serializerOptions);

            return RetryOnExceptionAsync(5, () =>
                 WriteResultsToSql(
                    utcNow,
                    sqlConnectionString,
                    tableName,
                    session,
                    category,
                    scenario,
                    hardware,
                    architecture,
                    operatingSystem,
                    document
                    )
                , 5000);
        }

        public static async Task InitializeDatabaseAsync(string connectionString, string tableName)
        {
            var createCmd =
                @"
                IF OBJECT_ID(N'dbo." + tableName + @"', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[" + tableName + @"](
                        [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [Excluded] [bit] DEFAULT 0,
                        [DateTime] [datetimeoffset](7) NOT NULL,
                        [Session] [nvarchar](200) NOT NULL,
                        [Category] [nvarchar](200) NOT NULL,
                        [Scenario] [nvarchar](200) NOT NULL,
                        [Hardware] [nvarchar](50) NOT NULL,
                        [Architecture] [nvarchar](128) NOT NULL,
                        [OperatingSystem] [nvarchar](50) NOT NULL,
                        [Document] [nvarchar](max) NOT NULL
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

        private static async Task WriteResultsToSql(
            DateTime utcNow,
            string connectionString,
            string tableName,
            string session,
            string category,
            string scenario,
            string hardware,
            string architecture,
            string operatingSystem,
            string document
            )
        {

            var insertCmd =
                @"
                INSERT INTO [dbo].[" + tableName + @"]
                           ([DateTime]
                           ,[Session]
                           ,[Category]
                           ,[Scenario]
                           ,[Hardware]
                           ,[Architecture]
                           ,[OperatingSystem]
                           ,[Document])
                     VALUES
                           (@DateTime
                           ,@Session
                           ,@Category
                           ,@Scenario
                           ,@Hardware
                           ,@Architecture
                           ,@OperatingSystem
                           ,@Document)
                ";

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var transaction = connection.BeginTransaction();

                try
                {
                    var command = new SqlCommand(insertCmd, connection, transaction);
                    var p = command.Parameters;
                    p.AddWithValue("@DateTime", utcNow);
                    p.AddWithValue("@Session", session);
                    p.AddWithValue("@Category", category ?? "");
                    p.AddWithValue("@Scenario", scenario ?? "");
                    p.AddWithValue("@Hardware", hardware ?? "");
                    p.AddWithValue("@Architecture", architecture ?? "");
                    p.AddWithValue("@OperatingSystem", operatingSystem ?? "");
                    p.AddWithValue("@Document", document);

                    await command.ExecuteNonQueryAsync();

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
                finally
                {
                    transaction.Dispose();
                }
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
    }
}
