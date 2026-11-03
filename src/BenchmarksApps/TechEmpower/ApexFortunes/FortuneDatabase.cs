using Apex.PgClient;
using Apex.SqlClient;

namespace ApexFortunes;

internal sealed class FortuneDatabase : IAsyncDisposable
{
    private static readonly ReadOnlyMemory<byte> s_additionalFortune =
        "Additional fortune added at request time."u8.ToArray();
    private readonly PgPipelinePool _pool;
    private readonly ISqlPreparedStatement _statement;

    private FortuneDatabase(
        PgPipelinePool pool,
        ISqlPreparedStatement statement)
    {
        _pool = pool;
        _statement = statement;
    }

    public static async ValueTask<FortuneDatabase> CreateAsync(
        IConfiguration configuration)
    {
        var connectionString = configuration["CONNECTION_STRING"] ??
            throw new InvalidOperationException("CONNECTION_STRING is required.");
        var connectionCount = configuration.GetValue("APEX_CONNECTIONS", 56);
        var pipeliningLimit = configuration.GetValue("APEX_PIPELINING", 64);
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
            return new FortuneDatabase(pool, statement);
        }
        catch
        {
            await pool.DisposeAsync();
            throw;
        }
    }

    public async ValueTask<List<Fortune>> LoadAsync()
    {
        // Benchmark requirements explicitly prohibit pre-initializing the list size.
        List<Fortune> fortunes = [];
        await _statement.CollectAsync(
            fortunes,
            static (results, row) => results.Add(new Fortune(
                row.GetInt32(0),
                row.Get<ReadOnlyMemory<byte>>(1))));
        fortunes.Add(new Fortune(0, s_additionalFortune));
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
