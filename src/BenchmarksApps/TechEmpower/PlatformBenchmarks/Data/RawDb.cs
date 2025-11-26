// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

// ReSharper disable UseAwaitUsing

namespace PlatformBenchmarks
{
    public sealed class RawDb
    {
        private readonly MemoryCache _cache
            = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(60) });

#if NET7_0_OR_GREATER
        private readonly NpgsqlDataSource _dataSource;
#else
        private readonly string _connectionString;
#endif

        public RawDb(AppSettings appSettings)
        {
#if NET8_0_OR_GREATER
            _dataSource = new NpgsqlSlimDataSourceBuilder(appSettings.ConnectionString).Build();
#elif NET7_0
            _dataSource = NpgsqlDataSource.Create(appSettings.ConnectionString);
#else
            _connectionString = appSettings.ConnectionString;
#endif
        }

        public async Task<World> LoadSingleQueryRow()
        {
            using var db = CreateConnection();
            await db.OpenAsync();

            var (cmd, _) = CreateReadCommand(db);
            using var command = cmd;

            return await ReadSingleRow(cmd);
        }

        public Task<CachedWorld[]> LoadCachedQueries(int count)
        {
            var result = new CachedWorld[count];
            var cacheKeys = _cacheKeys;
            var cache = _cache;

            for (var i = 0; i < result.Length; i++)
            {
                var id = Random.Shared.Next(1, 10001);
                var key = cacheKeys[id];
                if (cache.TryGetValue(key, out var cached))
                {
                    result[i] = (CachedWorld)cached;
                }
                else
                {
                    return LoadUncachedQueries(id, i, count, this, result);
                }
            }

            return Task.FromResult(result);

            static async Task<CachedWorld[]> LoadUncachedQueries(int id, int i, int count, RawDb rawdb, CachedWorld[] result)
            {
                using var db = rawdb.CreateConnection();
                await db.OpenAsync();

                var (cmd, idParameter) = rawdb.CreateReadCommand(db);
                using var command = cmd;
                async Task<CachedWorld> create(ICacheEntry _) => await ReadSingleRow(cmd);

                var cacheKeys = _cacheKeys;
                var key = cacheKeys[id];

                idParameter.TypedValue = id;

                for (; i < result.Length; i++)
                {
                    result[i] = await rawdb._cache.GetOrCreateAsync(key, create);

                    id = Random.Shared.Next(1, 10001);
                    idParameter.TypedValue = id;
                    key = cacheKeys[id];
                }

                return result;
            }
        }

        public async Task PopulateCache()
        {
            using var db = CreateConnection();
            await db.OpenAsync();

            var (cmd, idParameter) = CreateReadCommand(db);
            using var command = cmd;

            var cacheKeys = _cacheKeys;
            var cache = _cache;
            for (var i = 1; i < 10001; i++)
            {
                idParameter.TypedValue = i;
                cache.Set<CachedWorld>(cacheKeys[i], await ReadSingleRow(cmd));
            }

            Console.WriteLine("Caching Populated");
        }

#if NET7_0_OR_GREATER
        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var results = new World[count];

            using var connection = await _dataSource.OpenConnectionAsync();

            using var batch = new NpgsqlBatch(connection)
            {
                // Inserts a PG Sync message between each statement in the batch, required for compliance with
                // TechEmpower general test requirement 7
                // https://github.com/TechEmpower/FrameworkBenchmarks/wiki/Project-Information-Framework-Tests-Overview
                EnableErrorBarriers = true
            };

            for (var i = 0; i < count; i++)
            {
                batch.BatchCommands.Add(new()
                {
                    CommandText = "SELECT id, randomnumber FROM world WHERE id = $1",
                    Parameters = { new NpgsqlParameter<int> { TypedValue = Random.Shared.Next(1, 10001) } }
                });
            }

            using var reader = await batch.ExecuteReaderAsync();

            for (var i = 0; i < count; i++)
            {
                await reader.ReadAsync();
                results[i] = new World { Id = reader.GetInt32(0), RandomNumber = reader.GetInt32(1) };
                await reader.NextResultAsync();
            }

            return results;
        }
#else
        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var results = new World[count];

            using var db = CreateConnection();
            await db.OpenAsync();

            var (cmd, idParameter) = CreateReadCommand(db);
            using var command = cmd;

            for (var i = 0; i < results.Length; i++)
            {
                results[i] = await ReadSingleRow(cmd);
                idParameter.TypedValue = Random.Shared.Next(1, 10001);
            }

            return results;
        }
#endif

        public async Task<World[]> LoadMultipleUpdatesRows(int count)
        {
            var results = new World[count];

            using var connection = CreateConnection();
            await connection.OpenAsync();

            using (var batch = new NpgsqlBatch(connection))
            {
                // Inserts a PG Sync message between each statement in the batch, required for compliance with
                // TechEmpower general test requirement 7
                // https://github.com/TechEmpower/FrameworkBenchmarks/wiki/Project-Information-Framework-Tests-Overview
                batch.EnableErrorBarriers = true;

                var ids = new int[count];
                for (var i = 0; i < count; i++)
                {
                    ids[i] = Random.Shared.Next(1, 10001);
                }
                Array.Sort(ids);
                
                for (var i = 0; i < count; i++)
                {
                    batch.BatchCommands.Add(new()
                    {
                        CommandText = "SELECT id, randomnumber FROM world WHERE id = $1",
                        Parameters = { new NpgsqlParameter<int> { TypedValue = ids[i] } }
                    });
                }

                using var reader = await batch.ExecuteReaderAsync();

                for (var i = 0; i < count; i++)
                {
                    await reader.ReadAsync();
                    results[i] = new World { Id = reader.GetInt32(0), RandomNumber = reader.GetInt32(1) };
                    await reader.NextResultAsync();
                }
            }

            var numbers = new int[count];
            for (var i = 0; i < count; i++)
            {
                var randomNumber = Random.Shared.Next(1, 10001);
                results[i].RandomNumber = randomNumber;
                numbers[i] = randomNumber;
            }

            var update = "UPDATE world w SET randomnumber = u.new_val FROM (SELECT unnest($1) as id, unnest($2) as new_val) u WHERE w.id = u.id";

            using var updateCmd = new NpgsqlCommand(update, connection);
            updateCmd.Parameters.Add(new NpgsqlParameter<int[]> { TypedValue = ids });
            updateCmd.Parameters.Add(new NpgsqlParameter<int[]> { TypedValue = numbers });

            await updateCmd.ExecuteNonQueryAsync();

            return results;
        }

        public async Task<List<FortuneUtf8>> LoadFortunesRows()
        {
            // Benchmark requirements explicitly prohibit pre-initializing the list size
            var result = new List<FortuneUtf8>();

            using (var db = CreateConnection())
            {
                await db.OpenAsync();

                using var cmd = new NpgsqlCommand("SELECT id, message FROM fortune", db);
                using var rdr = await cmd.ExecuteReaderAsync();

                while (await rdr.ReadAsync())
                {
                    result.Add(new FortuneUtf8
                    (
                        id: rdr.GetInt32(0),
                        message: rdr.GetFieldValue<byte[]>(1)
                    ));
                }
            }

            result.Add(new FortuneUtf8(id: 0, AdditionalFortune));
            result.Sort();

            return result;
        }

        public Task<List<FortuneUtf8>> LoadFortunesRowsNoDb()
        {
            // Benchmark requirements explicitly prohibit pre-initializing the list size
            var result = new List<FortuneUtf8>
            {
                new(1, "fortune: No such file or directory"u8.ToArray()),
                new(2, "A computer scientist is someone who fixes things that aren't broken."u8.ToArray()),
                new(3, "After enough decimal places, nobody gives a damn."u8.ToArray()),
                new(4, "A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1"u8.ToArray()),
                new(5, "A computer program does what you tell it to do, not what you want it to do."u8.ToArray()),
                new(6, "Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen"u8.ToArray()),
                new(7, "Any program that runs right is obsolete."u8.ToArray()),
                new(8, "A list is only as strong as its weakest link. — Donald Knuth"u8.ToArray()),
                new(9, "Feature: A bug with seniority."u8.ToArray()),
                new(10, "Computers make very fast, very accurate mistakes."u8.ToArray()),
                new(11, "<script>alert(\"This should not be displayed in a browser alert box.\");</script>"u8.ToArray()),
                new(12, "フレームワークのベンチマーク"u8.ToArray()),
                new(0, AdditionalFortune)
            };

            result.Sort();

            return Task.FromResult(result);
        }

        private readonly byte[] AdditionalFortune = "Additional fortune added at request time."u8.ToArray();

        private (NpgsqlCommand readCmd, NpgsqlParameter<int> idParameter) CreateReadCommand(NpgsqlConnection connection)
        {
            var cmd = new NpgsqlCommand("SELECT id, randomnumber FROM world WHERE id = $1", connection);
            var parameter = new NpgsqlParameter<int> { TypedValue = Random.Shared.Next(1, 10001) };

            cmd.Parameters.Add(parameter);

            return (cmd, parameter);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static async Task<World> ReadSingleRow(NpgsqlCommand cmd)
        {
            using var rdr = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow);
            await rdr.ReadAsync();

            return new World
            {
                Id = rdr.GetInt32(0),
                RandomNumber = rdr.GetInt32(1)
            };
        }

        private NpgsqlConnection CreateConnection()
#if NET7_0_OR_GREATER
            => _dataSource.CreateConnection();
#else
            => new(_connectionString);
#endif

        private static readonly object[] _cacheKeys = Enumerable.Range(0, 10001).Select((i) => new CacheKey(i)).ToArray();

        public sealed class CacheKey : IEquatable<CacheKey>
        {
            private readonly int _value;

            public CacheKey(int value)
                => _value = value;

            public bool Equals(CacheKey key)
                => key._value == _value;

            public override bool Equals(object obj)
                => ReferenceEquals(obj, this);

            public override int GetHashCode()
                => _value;

            public override string ToString()
                => _value.ToString();
        }
    }
}
