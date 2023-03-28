using BlazorUnited.Models;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlazorUnited.Database;

public sealed class Db : IAsyncDisposable
{
    private static readonly Comparison<Fortune> FortuneSortComparison = (a, b) => string.CompareOrdinal(a.Message, b.Message);

    private readonly NpgsqlDataSource _dataSource;

    public Db(AppSettings appSettings)
    {
        ArgumentException.ThrowIfNullOrEmpty(appSettings.ConnectionString);

        _dataSource = new NpgsqlSlimDataSourceBuilder(appSettings.ConnectionString).Build();
    }

    public async Task<List<Fortune>> LoadFortunesRows()
    {
        var result = new List<Fortune>();

        await using (var cmd = _dataSource.CreateCommand("SELECT id, message FROM fortune"))
        {
            await using var rdr = await cmd.ExecuteReaderAsync();

            while (await rdr.ReadAsync())
            {
                result.Add(new Fortune { Id = rdr.GetInt32(0), Message = rdr.GetString(1) });
            }
        }

        result.Add(new Fortune { Id = 0, Message = "Additional fortune added at request time." });
        result.Sort(FortuneSortComparison);

        return result;
    }

    public async Task<List<Fortune>> LoadFortunesRowsDapper()
    {
        await using var connection = _dataSource.CreateConnection();
        var result = (await connection.QueryAsync<Fortune>($"SELECT id, message FROM fortune")).ToList();

        result.Add(new Fortune { Id = 0, Message = "Additional fortune added at request time." });
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

        result.Add(new Fortune { Id = 0, Message = "Additional fortune added at request time." });
        result.Sort(FortuneSortComparison);

        return result;
    }

    ValueTask IAsyncDisposable.DisposeAsync() => _dataSource.DisposeAsync();
}
