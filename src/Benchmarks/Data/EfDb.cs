// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Benchmarks.Data
{
    public class EfDb
    {
        private readonly IRandom _random;
        private readonly ApplicationDbContext _dbContext;

        public EfDb(IRandom random, ApplicationDbContext dbContext)
        {
            _random = random;
            _dbContext = dbContext;
            _dbContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }

        public Task<World> LoadSingleQueryRow()
        {
            var id = _random.Next(1, 10001);
            
            return _dbContext.World.FirstAsync(w => w.Id == id);
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count, bool withTracking = false)
        {
            var result = new World[count];

            for (int i = 0; i < count; i++)
            {
                var id = _random.Next(1, 10001);
                var world = withTracking ? _dbContext.World.AsTracking() : _dbContext.World;
                result[i] = await world.FirstAsync(w => w.Id == id);
            }

            return result;
        }

        public async Task<World[]> LoadMultipleUpdatesRows(int count)
        {
            var result = await LoadMultipleQueriesRows(count, true);

            for (int i = 0; i < count; i++)
            {
                result[i].RandomNumber = _random.Next(1, 10001);
            }

            await _dbContext.SaveChangesAsync();
            return result;
        }

        public async Task<IEnumerable<Fortune>> LoadFortunesRows()
        {
            var result = await _dbContext.Fortune.ToListAsync();

            result.Add(new Fortune { Message = "Additional fortune added at request time." });
            result.Sort();

            return result;
        }
    }
}
