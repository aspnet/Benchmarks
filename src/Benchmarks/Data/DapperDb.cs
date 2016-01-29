// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Dapper;

namespace Benchmarks.Data
{
    public class DapperDb
    {
        private readonly Random _random = new Random();
        private readonly DbProviderFactory _dbProviderFactory;

        public DapperDb(DbProviderFactory dbProviderFactory)
        {
            _dbProviderFactory = dbProviderFactory;
        }

        public async Task<World> LoadSingleQueryRow(string connectionString)
        {
            using (var db = _dbProviderFactory.CreateConnection())
            {
                db.ConnectionString = connectionString;
                // note: don't need to open connection if only doing one thing; let dapper do it
                return await db.QueryFirstOrDefaultAsync<World>(
                    "SELECT [Id], [RandomNumber] FROM [World] WHERE [Id] = @Id",
                    new { Id = _random.Next(1, 10001) });
            }
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count, string connectionString)
        {
            var result = new World[count];

            using (var db = _dbProviderFactory.CreateConnection())
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

        public async Task<IEnumerable<Fortune>> LoadFortunesRows(string connectionString)
        {
            List<Fortune> result;

            using (var db = _dbProviderFactory.CreateConnection())
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
