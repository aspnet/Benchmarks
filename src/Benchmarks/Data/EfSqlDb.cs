// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace Benchmarks.Data
{
    public class EfSqlDb : IDb
    {
        private readonly IRandom _random;
        private readonly ApplicationDbContext _dbContext;

        public EfSqlDb(IRandom random, ApplicationDbContext dbContext)
        {
            _random = random;
            _dbContext = dbContext;
        }

        private static readonly Func<ApplicationDbContext, int, AsyncEnumerable<World>> _firstWorldQuery
            = EF.CompileAsyncQuery((ApplicationDbContext context, int id)
                => context.World.FromSql("SELECT id, randomnumber FROM world WHERE Id = {0}", id));

        private Task<World> FetchSingleQueryRow(int id)
        {
            return ((IAsyncEnumerableAccessor<World>)_firstWorldQuery(_dbContext, id)).AsyncEnumerable.First(); // EF #7097, #7098
        }

        public Task<World> LoadSingleQueryRow()
        {
            return FetchSingleQueryRow(_random.Next(1, 10001));
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var results = new World[count];

            for (var i = 0; i < count; i++)
            {
                results[i] = await FetchSingleQueryRow(_random.Next(1, 10001));
            }

            return results;
        }

        public async Task<World[]> LoadMultipleUpdatesRows(int count)
        {
            const string sqlFormat = "UPDATE world SET randomnumber={{{0}}} WHERE id={{{1}}};";

            var results = new World[count];
            var parameters = new object[count * 2]; // EF #7100
            var sql = new StringBuilder(sqlFormat.Length * count);

            for (var i = 0; i < count; i++)
            {
                results[i] = await FetchSingleQueryRow(_random.Next(1, 10001));
                results[i].RandomNumber = _random.Next(1, 10001);
            }

            // postgres has problems with deadlocks when these aren't sorted
            Array.Sort(results, (a, b) => a.Id.CompareTo(b.Id));

            for (int i = 0, j = 0; i < results.Length; i++, j += 2)
            {
                parameters[j] = results[i].Id;
                parameters[j + 1] = results[i].RandomNumber;

                sql.AppendFormat(sqlFormat, j, j+1).AppendLine();
            }

            await _dbContext.Database.ExecuteSqlCommandAsync(sql.ToString(), default(CancellationToken), parameters);

            return results;
        }

        private static readonly Func<ApplicationDbContext, AsyncEnumerable<Fortune>> _fortunesQuery
            = EF.CompileAsyncQuery((ApplicationDbContext context) => context.Fortune); // EF #1862

        public async Task<IEnumerable<Fortune>> LoadFortunesRows()
        {
            var result = await _fortunesQuery(_dbContext).ToListAsync();

            result.Add(new Fortune { Message = "Additional fortune added at request time." });
            result.Sort();

            return result;
        }
    }
}
