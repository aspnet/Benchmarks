using System.Data.Common;
using Dapper;
using Minimal.Models;

namespace Minimal.Database;

public class Db
{
    private static readonly Comparison<Fortune> FortuneSortComparison = (a, b) => string.CompareOrdinal(a.Message, b.Message);

    private static readonly Random _random = Random.Shared;

    private readonly DbProviderFactory _dbProviderFactory;
    private readonly string _connectionString;

    public Db(AppSettings appSettings)
    {
        ArgumentException.ThrowIfNullOrEmpty(appSettings.ConnectionString);

        _dbProviderFactory = Npgsql.NpgsqlFactory.Instance;
        _connectionString = appSettings.ConnectionString;
    }

    public Task<World> LoadSingleQueryRow()
    {
        using var db = _dbProviderFactory.CreateConnection();
        db!.ConnectionString = _connectionString;

        // Note: Don't need to open connection if only doing one thing; let dapper do it
        return ReadSingleRow(db);
    }

    static Task<World> ReadSingleRow(DbConnection db)
    {
        return db.QueryFirstOrDefaultAsync<World>(
                "SELECT id, randomnumber FROM world WHERE id = @Id",
                new { Id = _random.Next(1, 10001) });
    }

    public async Task<World[]> LoadMultipleQueriesRows(int count)
    {
        if (count <= 0)
        {
            count = 1;
        }
        else if (count > 500)
        {
            count = 500;
        }

        var results = new World[count];
        using var db = _dbProviderFactory.CreateConnection();

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
        if (count <= 0)
        {
            count = 1;
        }
        else if (count > 500)
        {
            count = 500;
        }

        var parameters = new Dictionary<string, object>();

        using var db = _dbProviderFactory.CreateConnection();

        db!.ConnectionString = _connectionString;
        await db.OpenAsync();

        var results = new World[count];
        for (var i = 0; i < count; i++)
        {
            results[i] = await ReadSingleRow(db);
        }

        for (var i = 0; i < count; i++)
        {
            var randomNumber = _random.Next(1, 10001);
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

        using var db = _dbProviderFactory.CreateConnection();

        db!.ConnectionString = _connectionString;

        // Note: don't need to open connection if only doing one thing; let dapper do it
        result = (await db.QueryAsync<Fortune>("SELECT id, message FROM fortune")).AsList();

        result.Add(new Fortune(0, "Additional fortune added at request time."));
        result.Sort(FortuneSortComparison);

        return result;
    }
}