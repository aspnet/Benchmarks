using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rewrite.Configuration;

namespace Rewrite.Data
{

    public class EfDb : IDb
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly byte[] AdditionalFortune = "Additional fortune added at request time."u8.ToArray();
        public EfDb(ApplicationDbContext dbContext, IOptions<AppSettings> appSettings)
        {

            _dbContext = dbContext;
        }

        public Task<Fortune> LoadSingleQueryRow()
        {
            throw new NotImplementedException();
        }

        private static readonly Func<ApplicationDbContext, IAsyncEnumerable<Fortune>> _fortunesQuery
      = EF.CompileAsyncQuery((ApplicationDbContext context) => context.Fortune);

        public async Task<IEnumerable<Fortune>> LoadFortunesRows()
        {
            var result = new List<Fortune>();

            await foreach (var fortune in _fortunesQuery(_dbContext))
            {
                result.Add(fortune);
            }

            result.Add(new Fortune { Message = AdditionalFortune });

            result.Sort();

            return result;
        }
    }
}
