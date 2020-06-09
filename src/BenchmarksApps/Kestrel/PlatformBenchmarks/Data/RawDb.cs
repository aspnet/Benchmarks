// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;

namespace PlatformBenchmarks
{
    public class RawDb
    {
        private static readonly string[] Ids = Enumerable.Range(0, BatchUpdateString.MaxBatch).Select(i => $"@Id_{i}").ToArray();
        private static readonly string[] Randoms = Enumerable.Range(0, BatchUpdateString.MaxBatch).Select(i => $"@Random_{i}").ToArray();

        private readonly ConcurrentRandom _random;
        private readonly string _connectionString;

        public RawDb(ConcurrentRandom random, AppSettings appSettings)
        {
            _random = random;
            _connectionString = appSettings.ConnectionString;
        }

        public async Task<World> LoadSingleQueryRow()
        {
            var db = new NpgsqlConnection(_connectionString);

            try
            {
                if (db.State != ConnectionState.Open)
                {
                    await db.OpenAsync();
                }

                var (cmd, _) = CreateReadCommand(db);
                using (cmd)
                {
                    return await ReadSingleRow(cmd);
                }
            }
            finally
            {
                db.Close();
            }
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var result = new World[count];

            var db = new NpgsqlConnection(_connectionString);

            try
            {
                if (db.State != ConnectionState.Open)
                {
                    await db.OpenAsync();
                }

                var (cmd, idParameter) = CreateReadCommand(db);
                using (cmd)
                {
                    for (int i = 0; i < count; i++)
                    {
                        result[i] = await ReadSingleRow(cmd);
                        idParameter.TypedValue = _random.Next(1, 10001);
                    }
                }
            }
            finally
            {
                db.Close();
            }

            return result;
        }

        public async Task<World[]> LoadMultipleUpdatesRows(int count)
        {
            var results = new World[count];
            var db = new NpgsqlConnection(_connectionString);

            try
            {
                if (db.State != ConnectionState.Open)
                {
                    await db.OpenAsync();
                }

                var (queryCmd, queryParameter) = CreateReadCommand(db);
                using (queryCmd)
                {
                    for (int i = 0; i < count; i++)
                    {
                        results[i] = await ReadSingleRow(queryCmd);
                        queryParameter.TypedValue = _random.Next(1, 10001);
                    }
                }

                using (var updateCmd = new NpgsqlCommand(BatchUpdateString.Query(count), db))
                {
                    var ids = Ids;
                    var randoms = Randoms;

                    for (int i = 0; i < count; i++)
                    {
                        var randomNumber = _random.Next(1, 10001);

                        var idParameter = new NpgsqlParameter<int>(parameterName: ids[i], value: results[i].Id);
                        var randomParameter = new NpgsqlParameter<int>(parameterName: randoms[i], value: randomNumber);

                        updateCmd.Parameters.Add(idParameter);
                        updateCmd.Parameters.Add(randomParameter);

                        results[i].RandomNumber = randomNumber;
                    }

                    await updateCmd.ExecuteNonQueryAsync();
                    return results;
                }
            }
            finally
            {
                db.Close();
            }
        }

        public async Task<List<Fortune>> LoadFortunesRows()
        {
            var result = new List<Fortune>(20);

            var db = new NpgsqlConnection(_connectionString);

            try
            {
                if (db.State != ConnectionState.Open)
                {
                    await db.OpenAsync();
                }

                using (var cmd = new NpgsqlCommand("SELECT id, message FROM fortune", db))
                using (var rdr = await cmd.ExecuteReaderAsync())
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
            finally
            {
                db.Close();
            }

            result.Add(new Fortune { Message = "Additional fortune added at request time." });
            result.Sort();

            return result;
        }

        private (NpgsqlCommand readCmd, NpgsqlParameter<int> idParameter) CreateReadCommand(NpgsqlConnection connection)
        {
            var cmd = new NpgsqlCommand("SELECT id, randomnumber FROM world WHERE id = @Id", connection);
            var parameter = new NpgsqlParameter<int>(parameterName: "@Id", value: _random.Next(1, 10001));

            cmd.Parameters.Add(parameter);

            return (cmd, parameter);
        }

        private async Task<World> ReadSingleRow(NpgsqlCommand cmd)
        {
            using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow))
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
}
