// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Npgsql;

namespace PlatformBenchmarks
{
    public class RawDb
    {
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

                var (queryCmd, idParameter) = CreateReadCommand(db);
                using (queryCmd)
                {
                    for (int i = 0; i < count; i++)
                    {
                        results[i] = await ReadSingleRow(queryCmd);
                        idParameter.TypedValue = _random.Next(1, 10001);
                    }
                }

                using (var updateCmd = db.CreateCommand())
                {
                    updateCmd.CommandText = BatchUpdateString.Query(count);

                    for (int i = 0; i < count; i++)
                    {
                        var id = updateCmd.CreateParameter();
                        id.ParameterName = $"@Id_{i}";
                        id.DbType = DbType.Int32;
                        updateCmd.Parameters.Add(id);

                        var random = updateCmd.CreateParameter();
                        random.ParameterName = $"@Random_{i}";
                        random.DbType = DbType.Int32;
                        updateCmd.Parameters.Add(random);

                        var randomNumber = _random.Next(1, 10001);
                        id.Value = results[i].Id;
                        random.Value = randomNumber;
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
