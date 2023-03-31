using System.Data;
using System.Runtime.CompilerServices;

namespace Npgsql;

internal static class DataExtensions
{
    public static async Task<int> ExecuteAsync(this NpgsqlDataSource dataSource, string commandText, CancellationToken cancellationToken = default)
    {
        await using var cmd = dataSource.CreateCommand(commandText);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<object?> ExecuteScalarAsync(this NpgsqlDataSource dataSource, string commandText, CancellationToken cancellationToken = default)
    {
        await using var cmd = dataSource.CreateCommand(commandText);

        return await cmd.ExecuteScalarAsync(cancellationToken);
    }

    public static Task<int> ExecuteAsync(this NpgsqlDataSource dataSource, string commandText, params NpgsqlParameter[] parameters)
        => ExecuteAsync(dataSource, commandText, default, parameters);

    public static async Task<int> ExecuteAsync(this NpgsqlDataSource dataSource, string commandText, CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
    {
        await using var cmd = dataSource.CreateCommand(commandText, parameters);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<int> ExecuteAsync(this NpgsqlDataSource dataSource, string commandText, Action<NpgsqlParameterCollection> configureParameters, CancellationToken cancellationToken = default)
    {
        await using var cmd = dataSource.CreateCommand(commandText, configureParameters);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static Task<T?> QuerySingleAsync<T>(this NpgsqlDataSource dataSource, string commandText, params NpgsqlParameter[] parameters)
        where T : IDataReaderMapper<T>
        => QuerySingleAsync<T>(dataSource, commandText, default, parameters);

    public static async Task<T?> QuerySingleAsync<T>(this NpgsqlDataSource dataSource, string commandText, CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
        where T : IDataReaderMapper<T>
    {
        await using var reader = await dataSource.QuerySingleAsync(commandText, cancellationToken, parameters);

        return await reader.MapSingleAsync<T>();
    }

    public static async Task<T?> QuerySingleAsync<T>(this NpgsqlDataSource dataSource, string commandText, Action<NpgsqlParameterCollection>? configureParameters = null, CancellationToken cancellationToken = default)
        where T : IDataReaderMapper<T>
    {
        await using var cmd = dataSource.CreateCommand(commandText, configureParameters);

        await using var reader = await cmd.QuerySingleAsync(cancellationToken);

        return await reader.MapSingleAsync<T>();
    }

    public static IAsyncEnumerable<T> QueryAsync<T>(this NpgsqlDataSource dataSource, string commandText, params NpgsqlParameter[] parameters)
        where T : IDataReaderMapper<T>
        => QueryAsync<T>(dataSource, commandText, default, parameters);

    public static async IAsyncEnumerable<T> QueryAsync<T>(this NpgsqlDataSource dataSource, string commandText, [EnumeratorCancellation] CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
        where T : IDataReaderMapper<T>
    {
        var query = dataSource.QueryAsync<T>(commandText, parameterCollection => parameterCollection.AddRange(parameters), cancellationToken);

        await foreach (var item in query)
        {
            yield return item;
        }
    }

    public static async IAsyncEnumerable<T> QueryAsync<T>(this NpgsqlDataSource dataSource, string commandText, Action<NpgsqlParameterCollection>? configureParameters = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : IDataReaderMapper<T>
    {
        await using var cmd = dataSource.CreateCommand(commandText, configureParameters);

        await using var reader = await cmd.QueryAsync(cancellationToken);

        await foreach (var item in MapAsync<T>(reader))
        {
            yield return item;
        }
    }

    public static Task<T?> MapSingleAsync<T>(this NpgsqlDataReader reader)
        where T : IDataReaderMapper<T>
        => MapSingleAsync(reader, T.Map);

    public static async Task<T?> MapSingleAsync<T>(this NpgsqlDataReader reader, Func<NpgsqlDataReader, T> mapper)
    {
        if (!reader.HasRows)
        {
            return default;
        }

        await reader.ReadAsync();

        return mapper(reader);
    }

    public static IAsyncEnumerable<T> MapAsync<T>(this NpgsqlDataReader reader)
        where T : IDataReaderMapper<T>
        => MapAsync(reader, T.Map);

    public static async IAsyncEnumerable<T> MapAsync<T>(this NpgsqlDataReader reader, Func<NpgsqlDataReader, T> mapper)
    {
        if (!reader.HasRows)
        {
            yield break;
        }

        while (await reader.ReadAsync())
        {
            yield return mapper(reader);
        }
    }

    public static Task<NpgsqlDataReader> QuerySingleAsync(this NpgsqlDataSource dataSource, string commandText, params NpgsqlParameter[] parameters)
        => QuerySingleAsync(dataSource, commandText, default, parameters);

    public static Task<NpgsqlDataReader> QuerySingleAsync(this NpgsqlDataSource dataSource, string commandText, CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
        => QueryAsync(dataSource, commandText, CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken, parameters);

    public static Task<NpgsqlDataReader> QuerySingleAsync(this NpgsqlCommand command, CancellationToken cancellationToken = default)
        => QueryAsync(command, CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken);

    public static Task<NpgsqlDataReader> QueryAsync(this NpgsqlDataSource dataSource, string commandText, CommandBehavior commandBehavior, params NpgsqlParameter[] parameters)
        => QueryAsync(dataSource, commandText, commandBehavior, default, parameters);

    public static async Task<NpgsqlDataReader> QueryAsync(this NpgsqlDataSource dataSource, string commandText, CommandBehavior commandBehavior, CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
    {
        await using var cmd = dataSource.CreateCommand(commandText, parameters);

        return await cmd.ExecuteReaderAsync(commandBehavior, cancellationToken);
    }

    public static Task<NpgsqlDataReader> QueryAsync(this NpgsqlCommand command, CancellationToken cancellationToken = default)
        => QueryAsync(command, CommandBehavior.Default, cancellationToken);

    public static Task<NpgsqlDataReader> QueryAsync(this NpgsqlCommand command, CommandBehavior commandBehavior, CancellationToken cancellationToken = default)
        => command.ExecuteReaderAsync(commandBehavior, cancellationToken);

    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> enumerable, int? initialCapacity = null, CancellationToken cancellationToken = default)
    {
        var list = initialCapacity.HasValue ? new List<T>(initialCapacity.Value) : new List<T>();

        await foreach (var item in enumerable.WithCancellation(cancellationToken))
        {
            list.Add(item);
        }

        return list;
    }

    public static NpgsqlParameterCollection AddTyped<T>(this NpgsqlParameterCollection parameters, T? value)
    {
        parameters.Add(new NpgsqlParameter<T>
        {
            TypedValue = value
        });
        return parameters;
    }

    public static NpgsqlParameter<T> AsTypedDbParameter<T>(this T value)
    {
        var parameter = new NpgsqlParameter<T>
        {
            TypedValue = value
        };

        return parameter;
    }

    private static NpgsqlCommand CreateCommand(this NpgsqlDataSource dataSource, string commandText, params NpgsqlParameter[] parameters) =>
        ConfigureCommand(dataSource.CreateCommand(commandText), parameters);

    private static NpgsqlCommand ConfigureCommand(NpgsqlCommand cmd, NpgsqlParameter[] parameters) =>
        ConfigureCommand(cmd, parameterCollection =>
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                parameterCollection.Add(parameters[i]);
            }
        });

    private static NpgsqlCommand CreateCommand(this NpgsqlDataSource dataSource, string commandText, Action<NpgsqlParameterCollection>? configureParameters = null) =>
        ConfigureCommand(dataSource.CreateCommand(commandText), configureParameters);

    private static NpgsqlCommand ConfigureCommand(NpgsqlCommand cmd, Action<NpgsqlParameterCollection>? configureParameters = null)
    {
        if (configureParameters is not null)
        {
            configureParameters(cmd.Parameters);
        }

        return cmd;
    }
}

internal interface IDataReaderMapper<T> where T : IDataReaderMapper<T>
{
    abstract static T Map(NpgsqlDataReader dataReader);
}