// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<World> LoadSingleQueryRow() => ReadSingleRow(_connectionString);

        public Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var tasks = new Task<World>[count];

            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = ReadSingleRow(_connectionString);
            }

            return Task.WhenAll(tasks);
        }

        public async Task<World[]> LoadMultipleUpdatesRows(int count)
        {
            var results = await LoadMultipleQueriesRows(count);

            using (var db = new NpgsqlConnection(_connectionString))
            { 
                await db.OpenAsync();

                using (var updateCmd = new NpgsqlCommand(BatchUpdateString.Query(count), db))
                {
                    var ids = BatchUpdateString.Ids;
                    var randoms = BatchUpdateString.Randoms;

                    for (int i = 0; i < results.Length; i++)
                    {
                        var randomNumber = _random.Next(1, 10001);

                        updateCmd.Parameters.Add(new NpgsqlParameter<int>(parameterName: ids[i], value: results[i].Id));
                        updateCmd.Parameters.Add(new NpgsqlParameter<int>(parameterName: randoms[i], value: randomNumber));

                        results[i].RandomNumber = randomNumber;
                    }

                    await updateCmd.ExecuteNonQueryAsync();
                }
            }

            return results;
        }

        public async Task<List<Fortune>> LoadFortunesRows()
        {
            var result = new List<Fortune>(20);

            using (var db = new NpgsqlConnection(_connectionString))
            {
                await db.OpenAsync();

                using (var cmd = new NpgsqlCommand("SELECT id, message FROM fortune", db))
                using (var rdr = await cmd.ExecuteReaderAsync())
                {
                    while (await rdr.ReadAsync())
                    {
                        result.Add(new Fortune
                        (
                            id: rdr.GetInt32(0),
                            message: rdr.GetString(1)
                        ));
                    }
                }
            }

            result.Add(new Fortune(id: 0, message: "Additional fortune added at request time." ));
            result.Sort();

            return result;
        }

        private async Task<World> ReadSingleRow(string connectionString)
        {
            using (var db = new NpgsqlConnection(connectionString))
            {
                await db.OpenAsync();

                using (var cmd = CreateReadCommand(db))
                using (var rdr = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.Default))
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

        private NpgsqlCommand CreateReadCommand(NpgsqlConnection connection)
        {
            var cmd = new NpgsqlCommand("SELECT id, randomnumber FROM world WHERE id = @Id", connection);

            cmd.Parameters.Add(new NpgsqlParameter<int>(parameterName: "@Id", value: _random.Next(1, 10001)));

            return cmd;
        }
    }
}
