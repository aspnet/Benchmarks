using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using MvcFull.Models;

namespace MvcFull.Database
{
    public class Db
    {
        private readonly ApplicationDbContext _dbContext;

        public Db(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<World> LoadSingleQueryRow()
        {
            var random = new Random();
            var id = random.Next(1, 10001);

            return await _dbContext.Worlds.FirstAsync(w => w.Id == id);
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            count = count < 1 ? 1 : count > 500 ? 500 : count;

            var result = new World[count];
            var random = new Random();

            for (var i = 0; i < count; i++)
            {
                var id = random.Next(1, 10001);
                result[i] = await _dbContext.Worlds.FirstAsync(w => w.Id == id);
            }

            return result;
        }

        public async Task<World[]> LoadMultipleUpdatesRows(int count)
        {
            count = count < 1 ? 1 : count > 500 ? 500 : count;

            var results = new World[count];
            var random = new Random();

            for (var i = 0; i < count; i++)
            {
                var id = random.Next(1, 10001);
                var result = await _dbContext.Worlds.FirstAsync(w => w.Id == id);
                result.RandomNumber = random.Next(1, 10001);

                // Mark as modified
                _dbContext.Entry(result).State = EntityState.Modified;

                results[i] = result;
            }

            await _dbContext.SaveChangesAsync();

            return results;
        }

        public async Task<List<Fortune>> LoadFortunesRows()
        {
            var result = await _dbContext.Fortunes.ToListAsync();

            result.Add(new Fortune { Message = "Additional fortune added at request time." });

            result.Sort();

            return result;
        }
    }
}
