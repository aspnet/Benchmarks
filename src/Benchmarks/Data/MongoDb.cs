// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace Benchmarks.Data
{
    public class MongoDb : IDb
    {
        private readonly IRandom _random;
        private readonly IMongoCollection<Fortune> _fortuneCollection;
        private readonly IMongoCollection<World> _worldCollection;

        public MongoDb(IRandom random, IMongoCollection<Fortune> fortuneCollection, IMongoCollection<World> worldCollection)
        {
            _random = random;
            _fortuneCollection = fortuneCollection;
            _worldCollection = worldCollection;
        }

        public async Task<IEnumerable<Fortune>> LoadFortunesRows()
        {
            var result = await _fortuneCollection.Find(FilterDefinition<Fortune>.Empty).ToListAsync();

            result.Add(new Fortune { Message = "Additional fortune added at request time." });
            result.Sort();

            return result;
        }

        public Task<World[]> LoadMultipleUpdatesRows(int count)
        {
            throw new NotImplementedException();
        }

        public Task<World> LoadSingleQueryRow()
        {
            return _worldCollection.Find(x => x.Id == _random.Next(1, 10001)).FirstOrDefaultAsync();
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var results = new World[count];

            for (int i = 0; i < count; i++)
            {
                results[i] = await _worldCollection.Find(x => x.Id == _random.Next(1, 10001)).FirstOrDefaultAsync();
            }

            return results;
        }
    }
}
