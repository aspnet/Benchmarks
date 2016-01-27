// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace Benchmarks.Data
{
    public static class DapperDb
    {
        private static readonly Random _random = new Random();

        public static async Task<World> LoadSingleQueryRow(string connectionString, DbProviderFactory dbProviderFactory)
        {
            using (var db = dbProviderFactory.CreateConnection())
            {
                db.ConnectionString = connectionString;
                // note: don't need to open connection if only doing one thing; let dapper do it
                return await db.QueryFirstOrDefaultAsync<World>(
                    "SELECT [Id], [RandomNumber] FROM [World] WHERE [Id] = @Id",
                    new { Id = _random.Next(1, 10001) });
            }
        }

        public static async Task<World[]> LoadMultipleQueriesRows(int count, string connectionString, DbProviderFactory dbProviderFactory)
        {
            var result = new World[count];

            using (var db = dbProviderFactory.CreateConnection())
            {
                db.ConnectionString = connectionString;
                await db.OpenAsync();

                for (int i = 0; i < count; i++)
                {
                    result[i] = await db.QueryFirstOrDefaultAsync<World>(
                        "SELECT [Id], [RandomNumber] FROM [World] WHERE [Id] = @Id",
                        new { Id = _random.Next(1, 10001) });
                }

                db.Close();
            }

            return result;
        }

        public static async Task<IEnumerable<Fortune>> LoadFortunesRows(string connectionString, DbProviderFactory dbProviderFactory)
        {
            List<Fortune> result;

            using (var db = dbProviderFactory.CreateConnection())
            {
                db.ConnectionString = connectionString;
                // Note: don't need to open connection if only doing one thing; let dapper do it
                result = (await db.QueryAsync<Fortune>("SELECT [Id], [Message] FROM [Fortune]")).AsList();
            }

            result.Add(new Fortune { Message = "Additional fortune added at request time." });
            result.Sort();

            return result;
        }
    }
}
