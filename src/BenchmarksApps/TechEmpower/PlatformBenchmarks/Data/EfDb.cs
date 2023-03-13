using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace PlatformBenchmarks
{
    public sealed class EfDb
    {
        private static PooledDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public EfDb(AppSettings appSettings)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

            optionsBuilder
                .UseNpgsql(appSettings.ConnectionString, o => o.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d)))
                .EnableThreadSafetyChecks(false);

            var extension = (optionsBuilder.Options.FindExtension<CoreOptionsExtension>() ?? new CoreOptionsExtension())
                .WithMaxPoolSize(1024);

            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

            var options = optionsBuilder.Options;
            _dbContextFactory = new PooledDbContextFactory<ApplicationDbContext>(options);
        }

        public async Task<List<FortuneEf>> LoadFortunesRows()
        {
            var result = new List<FortuneEf>();

            using (var dbContext = _dbContextFactory.CreateDbContext())
            {
                await foreach (var fortune in _fortunesQuery(dbContext))
                {
                    result.Add(fortune);
                }
            }

            result.Add(new FortuneEf { Message = "Additional fortune added at request time." });

            result.Sort();

            return result;
        }

        private static readonly Func<ApplicationDbContext, IAsyncEnumerable<FortuneEf>> _fortunesQuery
            = EF.CompileAsyncQuery((ApplicationDbContext context) => context.Fortune);
    }
}
