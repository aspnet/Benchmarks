using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Mvc.Models;

#pragma warning disable EF1001 // Using internal EF pooling APIs, can be cleaned up after 6.0.0-preview4

namespace Mvc.Database;

public class Db
{
    private readonly PooledDbContextFactory<ApplicationDbContext> _dbContextFactory;

    public Db(AppSettings appSettings)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        optionsBuilder
            .UseNpgsql(appSettings.ConnectionString, o => o.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d)))
            .EnableThreadSafetyChecks(false)
            ;

        var extension = (optionsBuilder.Options.FindExtension<CoreOptionsExtension>() ?? new CoreOptionsExtension())
            .WithMaxPoolSize(1024);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        var options = optionsBuilder.Options;
        _dbContextFactory = new PooledDbContextFactory<ApplicationDbContext>(
            new DbContextPool<ApplicationDbContext>(options));
    }

    public async Task<List<Fortune>> LoadFortunesRows()
    {
        var result = new List<Fortune>();

        using (var dbContext = _dbContextFactory.CreateDbContext())
        {
            await foreach (var fortune in _fortunesQuery(dbContext))
            {
                result.Add(fortune);
            }
        }

        result.Add(new Fortune { Message = "Additional fortune added at request time." });

        result.Sort();

        return result;
    }

    private static readonly Func<ApplicationDbContext, IAsyncEnumerable<Fortune>> _fortunesQuery
        = EF.CompileAsyncQuery((ApplicationDbContext context) => context.Fortunes);
}
