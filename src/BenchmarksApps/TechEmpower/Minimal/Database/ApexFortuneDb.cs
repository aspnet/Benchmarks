using Apex.PgClient;
using Apex.SqlClient;
using Minimal.Models;

namespace Minimal.Database;

public sealed class ApexFortuneDb : IAsyncDisposable
{
    private static readonly ReadOnlyMemory<byte> s_additionalFortune =
        "Additional fortune added at request time."u8.ToArray();
    private readonly PgPipelinePool _pool;
    private readonly ISqlPreparedStatement _statement;

    private ApexFortuneDb(
        PgPipelinePool pool,
        ISqlPreparedStatement statement)
    {
        _pool = pool;
        _statement = statement;
    }

    public static async ValueTask<ApexFortuneDb> CreateAsync(
        string connectionString,
        int connectionCount,
        int pipeliningLimit)
    {
        var options = PgConnectOptions.Parse(connectionString) with
        {
            PipeliningLimit = pipeliningLimit,
        };
        var pool = await PgPipelinePool.CreateAsync(
            options,
            new SqlPipelinePoolOptions { ConnectionCount = connectionCount });
        try
        {
            var statement = await pool.PrepareAsync(
                "SELECT id, message FROM fortune");
            return new ApexFortuneDb(pool, statement);
        }
        catch
        {
            await pool.DisposeAsync();
            throw;
        }
    }

    public async ValueTask<List<ApexFortune>> LoadAsync()
    {
        // Benchmark requirements explicitly prohibit pre-initializing the list size.
        List<ApexFortune> fortunes = [];
        await _statement.CollectAsync(
            fortunes,
            static (results, row) => results.Add(new ApexFortune(
                row.GetInt32(0),
                row.Get<ReadOnlyMemory<byte>>(1))));
        fortunes.Add(new ApexFortune(0, s_additionalFortune));
        fortunes.Sort();
        return fortunes;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _statement.DisposeAsync();
        }
        finally
        {
            await _pool.DisposeAsync();
        }
    }
}
