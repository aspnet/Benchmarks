using Dapper;
using Microsoft.EntityFrameworkCore;
using Mvc.Models;
using Npgsql;

namespace Mvc.Database;

public sealed class DbDapper
{
    private static readonly Comparison<Fortune> FortuneSortComparison = (a, b) => string.CompareOrdinal(a.Message, b.Message);

    private readonly NpgsqlDataSource _dataSource;

    public DbDapper(AppSettings appSettings)
    {
        ArgumentException.ThrowIfNullOrEmpty(appSettings.ConnectionString);

#if NET8_0_OR_GREATER
        _dataSource = new NpgsqlSlimDataSourceBuilder(appSettings.ConnectionString).Build();
#else
        _dataSource = new NpgsqlDataSourceBuilder(appSettings.ConnectionString).Build();
#endif
    }

    public async Task<List<Fortune>> LoadFortunesRowsDapper()
    {
        await using var connection = _dataSource.CreateConnection();
        var result = (await connection.QueryAsync<Fortune>($"SELECT id, message FROM fortune")).ToList();

        result.Add(new Fortune { Id = 0, Message = "Additional fortune added at request time." });
        result.Sort(FortuneSortComparison);

        return result;
    }
}

public sealed class Db
{
    private readonly ApplicationDbContext _dbContext;

    public Db(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private static readonly Func<ApplicationDbContext, int, Task<World>> _firstWorldQuery
        = EF.CompileAsyncQuery((ApplicationDbContext context, int id)
            => context.Worlds.First(w => w.Id == id));

    public Task<World> LoadSingleQueryRow()
    {
        var id = Random.Shared.Next(1, 10001);

        return _firstWorldQuery(_dbContext, id);
    }

    public async Task<World[]> LoadMultipleQueriesRows(int count)
    {
        count = count < 1 ? 1 : count > 500 ? 500 : count;

        var result = new World[count];

        for (var i = 0; i < count; i++)
        {
            var id = Random.Shared.Next(1, 10001);

            result[i] = await _firstWorldQuery(_dbContext, id);
        }

        return result;
    }

    private static readonly Func<ApplicationDbContext, int, Task<World>> _firstWorldTrackedQuery
        = EF.CompileAsyncQuery((ApplicationDbContext context, int id)
            => context.Worlds.AsTracking().First(w => w.Id == id));

    public async Task<World[]> LoadMultipleUpdatesRows(int count)
    {
        count = count < 1 ? 1 : count > 500 ? 500 : count;

        var results = new World[count];

        for (var i = 0; i < count; i++)
        {
            var id = Random.Shared.Next(1, 10001);
            var result = await _firstWorldTrackedQuery(_dbContext, id);

            _dbContext.Entry(result).Property("RandomNumber").CurrentValue = Random.Shared.Next(1, 10001);
            results[i] = result;
        }

        await _dbContext.SaveChangesAsync();

        return results;
    }

    private static readonly Func<ApplicationDbContext, IAsyncEnumerable<Fortune>> _fortunesQuery
        = EF.CompileAsyncQuery((ApplicationDbContext context) => context.Fortunes);

    public async Task<IEnumerable<Fortune>> LoadFortunesRows()
    {
        var result = new List<Fortune>();

        await foreach (var fortune in _fortunesQuery(_dbContext))
        {
            result.Add(fortune);
        }

        result.Add(new Fortune { Message = "Additional fortune added at request time." });

        result.Sort();

        return result;
    }
}
