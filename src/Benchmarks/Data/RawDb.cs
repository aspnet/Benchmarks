// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.Extensions.Options;

namespace Benchmarks.Data
{
    public class RawDb : IDb
    {
        private readonly IRandom _random;
        private readonly DbProviderFactory _dbProviderFactory;
        private readonly string _connectionString;

        public RawDb(IRandom random, DbProviderFactory dbProviderFactory, IOptions<AppSettings> appSettings)
        {
            _random = random;
            _dbProviderFactory = dbProviderFactory;
            _connectionString = appSettings.Value.ConnectionString;
        }

        public async Task<World> LoadSingleQueryRow()
        {
            using (var db = _dbProviderFactory.CreateConnection())
            {
                return await ReadSingleRow(db);
            }
        }

        private async Task<World> ReadSingleRow(DbConnection connection)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT [Id], [RandomNumber] FROM [World] WHERE [Id] = @Id";
                var id = cmd.CreateParameter();
                id.ParameterName = "@Id";
                id.DbType = DbType.Int32;
                id.Value = _random.Next(1, 10001);
                cmd.Parameters.Add(id);

                connection.ConnectionString = _connectionString;
                await connection.OpenAsync();

                using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                {
                    await rdr.ReadAsync();

                    return new World
                    {
                        Id = rdr.GetInt32(0),
                        RandomNumber = rdr.GetInt32(1)
                    };
                }
            }
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var result = new World[count];

            using (var db = _dbProviderFactory.CreateConnection())
            {
                for (int i = 0; i < count; i++)
                {
                    result[i] = await ReadSingleRow(db);
                }

            }

            return result;
        }

        public async Task<World[]> LoadMultipleUpdatesRows(int count)
        {
            var result = await LoadMultipleQueriesRows(count);

            using (var db = _dbProviderFactory.CreateConnection())
            using (var cmd = db.CreateCommand())
            {
                db.ConnectionString = _connectionString;
                await db.OpenAsync();

                cmd.CommandText = "UPDATE world SET randomNumber = @Random WHERE id = @Id";
                var id = cmd.CreateParameter();
                id.ParameterName = "@Id";
                id.DbType = DbType.Int32;
                cmd.Parameters.Add(id);

                var random = cmd.CreateParameter();
                random.ParameterName = "@Random";
                id.DbType = DbType.Int32;
                cmd.Parameters.Add(random);

                for (int i = 0; i < count; i++)
                {
                    id.Value = result[i].Id;
                    var randomNumber = _random.Next(1, 10001);
                    random.Value = randomNumber;
                    using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        await rdr.ReadAsync();
                        result[i].RandomNumber = randomNumber;
                    }
                }

                db.Close();
            }

            return result;
        }

        public async Task<IEnumerable<Fortune>> LoadFortunesRows()
        {
            var result = new List<Fortune>();

            using (var db = _dbProviderFactory.CreateConnection())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT [Id], [Message] FROM [Fortune]";

                db.ConnectionString = _connectionString;
                await db.OpenAsync();

                using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                {
                    while (await rdr.ReadAsync())
                    {
                        result.Add(new Fortune
                        {
                            Id = rdr.GetInt32(0),
                            Message = rdr.GetString(1)
                        });
                    }
                }
            }

            result.Add(new Fortune { Message = "Additional fortune added at request time." });
            result.Sort();

            return result;
        }
    }
}
