// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Benchmarks.Data
{
    public static class EfDb
    {
        private static readonly Random _random = new Random();

        public static Task<World> LoadSingleQueryRow(ApplicationDbContext dbContext)
        {
            dbContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            var id = _random.Next(1, 10001);
            
            return dbContext.World.FirstAsync(w => w.Id == id);
        }

        public static async Task<World[]> LoadMultipleQueriesRows(int count, ApplicationDbContext dbContext)
        {
            dbContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            var result = new World[count];

            for (int i = 0; i < count; i++)
            {
                var id = _random.Next(1, 10001);
                result[i] = await dbContext.World.FirstAsync(w => w.Id == id);
            }

            return result;
        }

        public static async Task<IEnumerable<Fortune>> LoadFortunesRows(ApplicationDbContext dbContext)
        {
            dbContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            var result = await dbContext.Fortune.ToListAsync();

            result.Add(new Fortune { Message = "Additional fortune added at request time." });
            result.Sort();

            return result;
        }
    }
}
