using Dapper;
using Mvc.Models;
using Npgsql;

[module: DapperAot] // enable AOT Dapper support project-wide

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
        var result = (await connection.QueryAsync<Fortune>("SELECT id, message FROM fortune")).AsList();

        result.Add(new Fortune { Id = 0, Message = "Additional fortune added at request time." });
        result.Sort(FortuneSortComparison);

        return result;
    }
}
