using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Storage;

#pragma warning disable EF1001 // Using internal EF pooling APIs, can be cleaned up after 6.0.0-preview4

namespace PlatformBenchmarks
{
    public class EfDb
    {
        private static PooledDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public EfDb(AppSettings appSettings)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

            optionsBuilder
                .UseNpgsql(appSettings.ConnectionString
#if NET5_0_OR_GREATER
                    , o => o.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d))
#endif
                )
#if NET6_0_OR_GREATER
                            .DisableConcurrencyDetection()
#endif
                ;

            var extension = (optionsBuilder.Options.FindExtension<CoreOptionsExtension>() ?? new CoreOptionsExtension())
                .WithMaxPoolSize(1024);

            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

            var options = optionsBuilder.Options;
            _dbContextFactory = new PooledDbContextFactory<ApplicationDbContext>(
                new DbContextPool<ApplicationDbContext>(options));
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