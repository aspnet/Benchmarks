using BlazorSSR.Models;
using Dapper;
using Npgsql;

namespace BlazorSSR.Database;

public sealed class Db : IAsyncDisposable
{
    private static readonly Comparison<Fortune> FortuneSortComparison = (a, b) => string.CompareOrdinal(a.Message, b.Message);

    private readonly NpgsqlDataSource _dataSource;

    public Db(AppSettings appSettings)
    {
        ArgumentException.ThrowIfNullOrEmpty(appSettings.ConnectionString);

        // Debug: Log the connection string to see what we're actually getting
        Console.WriteLine("Env vars: ");
        foreach (var env in Environment.GetEnvironmentVariables())
        {
            Console.WriteLine(env.ToString());
        }
        Console.WriteLine("----------");

        Console.WriteLine($"ConnectionString: '{appSettings.ConnectionString}'");

#if NET8_0_OR_GREATER
        _dataSource = new NpgsqlSlimDataSourceBuilder(appSettings.ConnectionString).Build();
#else
        _dataSource = new NpgsqlDataSourceBuilder(appSettings.ConnectionString).Build();
#endif
    }

    [DapperAot, CacheCommand, StrictTypes, QueryColumns("id", "message")]
    public async Task<List<Fortune>> LoadFortunesRowsDapper()
    {
        await using var connection = _dataSource.CreateConnection();
        var result = (await connection.QueryAsync<Fortune>($"SELECT id, message FROM fortune")).AsList();

        result.Add(new() { Id = 0, Message = "Additional fortune added at request time." });
        result.Sort(FortuneSortComparison);

        return result;
    }

    public async Task<List<Fortune>> LoadFortunesRowsEf(AppDbContext dbContext)
    {
        var result = new List<Fortune>();

        await foreach (var fortune in AppDbContext.FortunesQuery(dbContext))
        {
            result.Add(fortune);
        }

        result.Add(new() { Id = 0, Message = "Additional fortune added at request time." });
        result.Sort(FortuneSortComparison);

        return result;
    }

    public Task<List<Fortune>> LoadFortunesRowsNoDb()
    {
        // Benchmark requirements explicitly prohibit pre-initializing the list size
        var result = new List<Fortune>
        {
            new() { Id = 1, Message = "fortune: No such file or directory" },
            new() { Id = 2, Message = "A computer scientist is someone who fixes things that aren't broken." },
            new() { Id = 3, Message = "After enough decimal places, nobody gives a damn." },
            new() { Id = 4, Message = "A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1" },
            new() { Id = 5, Message = "A computer program does what you tell it to do, not what you want it to do." },
            new() { Id = 6, Message = "Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen" },
            new() { Id = 7, Message = "Any program that runs right is obsolete." },
            new() { Id = 8, Message = "A list is only as strong as its weakest link. — Donald Knuth" },
            new() { Id = 9, Message = "Feature: A bug with seniority." },
            new() { Id = 10, Message = "Computers make very fast, very accurate mistakes." },
            new() { Id = 11, Message = "<script>alert(\"This should not be displayed in a browser alert box.\");</script>" },
            new() { Id = 12, Message = "フレームワークのベンチマーク" },
            new() { Id = 0, Message = "Additional fortune added at request time." }
        };

        result.Sort(FortuneSortComparison);

        return Task.FromResult(result);
    }

    ValueTask IAsyncDisposable.DisposeAsync() => _dataSource.DisposeAsync();
}
