using System.Data.Common;
using Dapper;
using Minimal.Models;

namespace Minimal.Database;

public class Db
{
    private static readonly Comparison<Fortune> FortuneSortComparison = (a, b) => string.CompareOrdinal(a.Message, b.Message);

    private readonly DbProviderFactory _dbProviderFactory;
    private readonly string _connectionString;

    public Db(AppSettings appSettings)
    {
        ArgumentException.ThrowIfNullOrEmpty(appSettings.ConnectionString);

        _dbProviderFactory = Npgsql.NpgsqlFactory.Instance;
        _connectionString = appSettings.ConnectionString;
    }

    public async Task<World> LoadSingleQueryRow()
    {
        await using var db = _dbProviderFactory.CreateConnection();
        db!.ConnectionString = _connectionString;

        // Note: Don't need to open connection if only doing one thing; let dapper do it
        return await ReadSingleRow(db);
    }

    static Task<World> ReadSingleRow(DbConnection db)
    {
        return db.QueryFirstOrDefaultAsync<World>(
                "SELECT id, randomnumber FROM world WHERE id = @Id",
                new { Id = Random.Shared.Next(1, 10001) });
    }

    public async Task<World[]> LoadMultipleQueriesRows(int count)
    {
        count = Math.Clamp(count, 1, 500);

        var results = new World[count];
        await using var db = _dbProviderFactory.CreateConnection();

        db!.ConnectionString = _connectionString;
        await db.OpenAsync();

        for (var i = 0; i < count; i++)
        {
            results[i] = await ReadSingleRow(db);
        }

        return results;
    }

    public async Task<World[]> LoadMultipleUpdatesRows(int count)
    {
        count = Math.Clamp(count, 1, 500);

        var parameters = new Dictionary<string, object>();

        await using var db = _dbProviderFactory.CreateConnection();

        db!.ConnectionString = _connectionString;
        await db.OpenAsync();

        var results = new World[count];
        for (var i = 0; i < count; i++)
        {
            results[i] = await ReadSingleRow(db);
        }

        for (var i = 0; i < count; i++)
        {
            var randomNumber = Random.Shared.Next(1, 10001);
            parameters[$"@Rn_{i}"] = randomNumber;
            parameters[$"@Id_{i}"] = results[i].Id;

            results[i].RandomNumber = randomNumber;
        }

        await db.ExecuteAsync(BatchUpdateString.Query(count), parameters);
        return results;
    }

    public async Task<List<Fortune>> LoadFortunesRows()
    {
        List<Fortune> result;

        await using var db = _dbProviderFactory.CreateConnection();

        db!.ConnectionString = _connectionString;

        // Note: don't need to open connection if only doing one thing; let dapper do it
        result = (await db.QueryAsync<Fortune>("SELECT id, message FROM fortune")).AsList();

        result.Add(new Fortune(0, "Additional fortune added at request time."));
        result.Sort(FortuneSortComparison);

        return result;
    }

    public Task<List<Fortune>> LoadFortunesRowsNoDb()
    {
        // Benchmark requirements explicitly prohibit pre-initializing the list size
        var result = new List<Fortune>
        {
            new(1, "fortune: No such file or directory"),
            new(2, "A computer scientist is someone who fixes things that aren't broken."),
            new(3, "After enough decimal places, nobody gives a damn."),
            new(4, "A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1"),
            new(5, "A computer program does what you tell it to do, not what you want it to do."),
            new(6, "Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen"),
            new(7, "Any program that runs right is obsolete."),
            new(8, "A list is only as strong as its weakest link. — Donald Knuth"),
            new(9, "Feature: A bug with seniority."),
            new(10, "Computers make very fast, very accurate mistakes."),
            new(11, "<script>alert(\"This should not be displayed in a browser alert box.\");</script>"),
            new(12, "フレームワークのベンチマーク"),
            new(0, "Additional fortune added at request time.")
        };

        result.Sort(FortuneSortComparison);

        return Task.FromResult(result);
    }
}