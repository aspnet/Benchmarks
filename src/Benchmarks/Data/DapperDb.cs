// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
#nullable enable

using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Dapper;
using Microsoft.Extensions.Options;

namespace Benchmarks.Data
{
    // Annotate is a "where this query comes from" hint, for DB debugging
    // [Annotate(false)] (not needed: default)
    // ReuseCommand allows DbCommand objects to be reused; this doesn't currently work well if the provider might change at runtime!
    // [ReuseCommand(true)] (not needed: default)
    public partial class DapperDb : IDb
    {
        private readonly IRandom _random;
        [Connection] // when a database is not provided (or turns out to be null), a contextual db/provider can be used
        private readonly DbProviderFactory _dbProviderFactory;
        [ConnectionString] // used by DapperAOT in conjuction with a discovered DbProviderFactory
        private readonly string _connectionString;

        public DapperDb(IRandom random, DbProviderFactory dbProviderFactory, IOptions<AppSettings> appSettings)
        {
            _random = random;
            _dbProviderFactory = dbProviderFactory;
            _connectionString = appSettings.Value.ConnectionString;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int NextRandom() => _random.Next(1, 10001);

        public async Task<IEnumerable<Fortune>> LoadFortunesRows()
        {
            List<Fortune> result = await ReadFortunesRows();
            result.Add(new Fortune { Message = "Additional fortune added at request time." });
            result.Sort();

            return result;
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var results = new World[count];
            using var db = _dbProviderFactory.CreateConnection()!;
            db.ConnectionString = _connectionString;
            await db.OpenAsync();

            for (int i = 0; i < count; i++)
            {
                results[i] = await ReadSingleRow(NextRandom(), db);
            }

            return results;
        }

        public async Task<World[]> LoadMultipleUpdatesRows(int count)
        {
            var parameters = new Dictionary<string, int>(2 * count);

            using var db = _dbProviderFactory.CreateConnection()!;
            db.ConnectionString = _connectionString;
            await db.OpenAsync();

            var results = new World[count];
            for (int i = 0; i < count; i++)
            {
                results[i] = await ReadSingleRow(NextRandom(), db);
            }

            for (int i = 0; i < count; i++)
            {
                var randomNumber = NextRandom();
                parameters[$"@Random_{i}"] = randomNumber;
                parameters[$"@Id_{i}"] = results[i].Id;

                results[i].RandomNumber = randomNumber;
            }

            await ExecuteBatch(BatchUpdateString.Query(count), parameters, db);
            return results;
        }

        public Task<World> LoadSingleQueryRow() => ReadSingleRow(NextRandom());

        [Command("SELECT id, randomnumber FROM world WHERE id = @id")]
        private partial Task<World> ReadSingleRow(int id, DbConnection? db = null);

        [Command("SELECT id, message FROM fortune")]
        private partial Task<List<Fortune>> ReadFortunesRows();

        [Command]
        private partial Task ExecuteBatch([Command] string command, Dictionary<string, int> parameters, DbConnection? db);
    }
}
