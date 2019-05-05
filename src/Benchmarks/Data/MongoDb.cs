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

        public async Task<List<Fortune>> LoadFortunesRows()
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
            var rnd = _random.Next(1, 10001);

            var filter = Builders<World>.Filter.Eq("_id", rnd);
            return _worldCollection.Find(filter).SingleAsync();
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var results = new World[count];

            for (int i = 0; i < count; i++)
            {
                var rnd = _random.Next(1, 10001);
                var filter = Builders<World>.Filter.Eq("_id", rnd);
                results[i] = await _worldCollection.Find(filter).SingleAsync();
            }

            return results;
        }
    }
}
