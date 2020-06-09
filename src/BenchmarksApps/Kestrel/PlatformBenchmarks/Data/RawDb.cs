// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Npgsql;

namespace PlatformBenchmarks
{
    public class RawDb
    {
        [ThreadStatic]
        private static IntReadCommandWrapper s_readCommand;

        private readonly ConcurrentRandom _random;
        private readonly string _connectionString;

        private IntReadCommandWrapper ReadCommand => s_readCommand ??= CreateReadCommandWrapper();

        public RawDb(ConcurrentRandom random, AppSettings appSettings)
        {
            _random = random;
            _connectionString = appSettings.ConnectionString;
        }

        public async Task<World> LoadSingleQueryRow()
        {
            var wrapper = ReadCommand;
            var cmd = wrapper.ReadCommand;
            var db = new NpgsqlConnection(_connectionString);
            cmd.Connection = db;

            try
            {
                if (db.State != ConnectionState.Open)
                {
                    await db.OpenAsync();
                }

                wrapper.IdParameter.Value = _random.Next(1, 10001);

                return await ReadSingleRow(cmd);
            }
            finally
            {
                cmd.Connection = null;
                db.Close();
            }
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var result = new World[count];

            var wrapper = ReadCommand;
            var cmd = wrapper.ReadCommand;
            var parameter = wrapper.IdParameter;
            var db = new NpgsqlConnection(_connectionString);
            cmd.Connection = db;

            try
            {
                if (db.State != ConnectionState.Open)
                {
                    await db.OpenAsync();
                }

                for (int i = 0; i < count; i++)
                {
                    result[i] = await ReadSingleRow(cmd);
                    parameter.TypedValue = _random.Next(1, 10001);
                }
            }
            finally
            {
                cmd.Connection = null;
                db.Close();
            }

            return result;
        }

        public async Task<World[]> LoadMultipleUpdatesRows(int count)
        {
            var wrapper = ReadCommand;
            var queryCmd = wrapper.ReadCommand;
            var queryParameter = wrapper.IdParameter;
            var db = new NpgsqlConnection(_connectionString);
            queryCmd.Connection = db;

            try
            {
                if (db.State != ConnectionState.Open)
                {
                    await db.OpenAsync();
                }

                using (var updateCmd = db.CreateCommand())
                {
                    var results = new World[count];
                    for (int i = 0; i < count; i++)
                    {
                        results[i] = await ReadSingleRow(queryCmd);
                        queryParameter.TypedValue = _random.Next(1, 10001);
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
                queryCmd.Connection = null;
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

        private static IntReadCommandWrapper CreateReadCommandWrapper()
        {
            var cmd = new NpgsqlCommand("SELECT id, randomnumber FROM world WHERE id = @Id");
            var parameter = new NpgsqlParameter<int>(parameterName: "@Id", value: -1);

            cmd.Parameters.Add(parameter);

            return new IntReadCommandWrapper(cmd, parameter);
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

        private class IntReadCommandWrapper
        {
            public IntReadCommandWrapper(NpgsqlCommand readCommand, NpgsqlParameter<int> idParameter)
            {
                ReadCommand = readCommand;
                IdParameter = idParameter;
            }

            internal NpgsqlCommand ReadCommand { get; }
            internal NpgsqlParameter<int> IdParameter { get; }
        }
    }
}
