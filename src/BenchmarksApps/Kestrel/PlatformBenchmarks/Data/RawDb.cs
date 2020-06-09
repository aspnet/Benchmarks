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

                using (var cmd = CreateReadCommand(db))
                {
                    return await ReadSingleRow(cmd);
                }
            }
            finally
            {
                db.Close();
            }
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

        private NpgsqlCommand CreateReadCommand(NpgsqlConnection connection)
        {
            var cmd = connection.CreateCommand();

            cmd.CommandText = "SELECT id, randomnumber FROM world WHERE id = @Id";

            cmd.Parameters.Add(
                new NpgsqlParameter<int>(parameterName: "@Id", value: _random.Next(1, 10001))
            );

            return cmd;
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

                using (var cmd = CreateReadCommand(db))
                {
                    for (int i = 0; i < count; i++)
                    {
                        result[i] = await ReadSingleRow(cmd);
                        cmd.Parameters["@Id"].Value = _random.Next(1, 10001);
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
            var db = new NpgsqlConnection(_connectionString);

            try
            {
                if (db.State != ConnectionState.Open)
                {
                    await db.OpenAsync();
                }

                using (var updateCmd = db.CreateCommand())
                using (var queryCmd = CreateReadCommand(db))
                {
                    var results = new World[count];
                    for (int i = 0; i < count; i++)
                    {
                        results[i] = await ReadSingleRow(queryCmd);
                        queryCmd.Parameters["@Id"].Value = _random.Next(1, 10001);
                    }

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

                using (var cmd = db.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, message FROM fortune";

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
            }
            finally
            {
                db.Close();
            }

            result.Add(new Fortune { Message = "Additional fortune added at request time." });
            result.Sort();

            return result;
        }
    }
}
