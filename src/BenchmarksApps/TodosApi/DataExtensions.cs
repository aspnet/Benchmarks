using System.Data;

namespace Npgsql;

internal static class DataExtensions
{
    public static async ValueTask OpenIfClosedAsync(this NpgsqlConnection connection)
    {
        if (connection.State == ConnectionState.Closed)
        {
            await connection.OpenAsync();
        }
    }

    public static async Task<int> ExecuteAsync(this NpgsqlConnection connection, string commandText)
    {
        await using var cmd = connection.CreateCommand(commandText);

        await connection.OpenIfClosedAsync();
        return await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<int> ExecuteAsync(this NpgsqlDataSource dataSource, string commandText)
    {
        await using var cmd = dataSource.CreateCommand(commandText);

        return await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<object?> ExecuteScalarAsync(this NpgsqlDataSource dataSource, string commandText)
    {
        await using var cmd = dataSource.CreateCommand(commandText);

        return await cmd.ExecuteScalarAsync(CancellationToken.None);
    }

    public static async Task<int> ExecuteAsync(this NpgsqlConnection connection, string commandText, params NpgsqlParameter[] parameters)
    {
        await using var cmd = connection.CreateCommand(commandText, parameters);

        await connection.OpenIfClosedAsync();
        return await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<int> ExecuteAsync(this NpgsqlDataSource dataSource, string commandText, params NpgsqlParameter[] parameters)
    {
        await using var cmd = dataSource.CreateCommand(commandText, parameters);

        return await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<int> ExecuteAsync(this NpgsqlConnection connection, string commandText, Action<NpgsqlParameterCollection> configureParameters)
    {
        await using var cmd = connection.CreateCommand(commandText, configureParameters);

        await connection.OpenIfClosedAsync();
        return await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<int> ExecuteAsync(this NpgsqlDataSource dataSource, string commandText, Action<NpgsqlParameterCollection> configureParameters)
    {
        await using var cmd = dataSource.CreateCommand(commandText, configureParameters);

        return await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<T?> QuerySingleAsync<T>(this NpgsqlConnection connection, string commandText, params NpgsqlParameter[] parameters)
        where T : IDataReaderMapper<T>
    {
        await connection.OpenIfClosedAsync();
        await using var reader = await connection.QuerySingleAsync(commandText, parameters);

        return await reader.MapSingleAsync<T>();
    }

    public static async Task<T?> QuerySingleAsync<T>(this NpgsqlDataSource dataSource, string commandText, params NpgsqlParameter[] parameters)
        where T : IDataReaderMapper<T>
    {
        await using var reader = await dataSource.QuerySingleAsync(commandText, parameters);

        return await reader.MapSingleAsync<T>();
    }

    public static async Task<T?> QuerySingleAsync<T>(this NpgsqlConnection connection, string commandText, Action<NpgsqlParameterCollection>? configureParameters = null)
        where T : IDataReaderMapper<T>
    {
        await using var cmd = connection.CreateCommand(commandText, configureParameters);

        await using var reader = await connection.QuerySingleAsync(cmd);

        return await reader.MapSingleAsync<T>();
    }

    public static async Task<T?> QuerySingleAsync<T>(this NpgsqlDataSource dataSource, string commandText, Action<NpgsqlParameterCollection>? configureParameters = null)
        where T : IDataReaderMapper<T>
    {
        await using var cmd = dataSource.CreateCommand(commandText, configureParameters);

        await using var reader = await cmd.QuerySingleAsync();

        return await reader.MapSingleAsync<T>();
    }

    public static async IAsyncEnumerable<T> QueryAsync<T>(this NpgsqlConnection connection, string commandText, params NpgsqlParameter[] parameters)
        where T : IDataReaderMapper<T>
    {
        var query = connection.QueryAsync<T>(commandText, parameterCollection => parameterCollection.AddRange(parameters));

        await foreach (var item in query)
        {
            yield return item;
        }
    }

    public static async IAsyncEnumerable<T> QueryAsync<T>(this NpgsqlDataSource dataSource, string commandText, params NpgsqlParameter[] parameters)
        where T : IDataReaderMapper<T>
    {
        var query = dataSource.QueryAsync<T>(commandText, parameterCollection => parameterCollection.AddRange(parameters));

        await foreach (var item in query)
        {
            yield return item;
        }
    }

    public static async IAsyncEnumerable<T> QueryAsync<T>(this NpgsqlConnection connection, string commandText, Action<NpgsqlParameterCollection>? configureParameters = null)
        where T : IDataReaderMapper<T>
    {
        await using var cmd = connection.CreateCommand(commandText, configureParameters);

        await connection.OpenIfClosedAsync();

        await using var reader = await connection.QueryAsync(cmd);

        await foreach (var item in MapAsync<T>(reader))
        {
            yield return item;
        }
    }

    public static async IAsyncEnumerable<T> QueryAsync<T>(this NpgsqlDataSource dataSource, string commandText, Action<NpgsqlParameterCollection>? configureParameters = null)
        where T : IDataReaderMapper<T>
    {
        await using var cmd = dataSource.CreateCommand(commandText, configureParameters);

        await using var reader = await cmd.QueryAsync();

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

    public static Task<NpgsqlDataReader> QuerySingleAsync(this NpgsqlConnection connection, string commandText, params NpgsqlParameter[] parameters)
        => QueryAsync(connection, commandText, CommandBehavior.SingleResult | CommandBehavior.SingleRow, parameters);

    public static Task<NpgsqlDataReader> QuerySingleAsync(this NpgsqlDataSource dataSource, string commandText, params NpgsqlParameter[] parameters)
        => QueryAsync(dataSource, commandText, CommandBehavior.SingleResult | CommandBehavior.SingleRow, parameters);

    public static Task<NpgsqlDataReader> QuerySingleAsync(this NpgsqlConnection connection, NpgsqlCommand command)
        => QueryAsync(connection, command, CommandBehavior.SingleResult | CommandBehavior.SingleRow);

    public static Task<NpgsqlDataReader> QuerySingleAsync(this NpgsqlCommand command)
        => QueryAsync(command, CommandBehavior.SingleResult | CommandBehavior.SingleRow);

    public static async Task<NpgsqlDataReader> QueryAsync(this NpgsqlConnection connection, string commandText, CommandBehavior commandBehavior, params NpgsqlParameter[] parameters)
    {
        await using var cmd = connection.CreateCommand(commandText, parameters);

        await connection.OpenIfClosedAsync();
        return await cmd.ExecuteReaderAsync(commandBehavior);
    }

    public static async Task<NpgsqlDataReader> QueryAsync(this NpgsqlDataSource dataSource, string commandText, CommandBehavior commandBehavior, params NpgsqlParameter[] parameters)
    {
        await using var cmd = dataSource.CreateCommand(commandText, parameters);

        return await cmd.ExecuteReaderAsync(commandBehavior);
    }

    public static Task<NpgsqlDataReader> QueryAsync(this NpgsqlConnection connection, NpgsqlCommand command)
        => QueryAsync(connection, command, CommandBehavior.Default);

    public static Task<NpgsqlDataReader> QueryAsync(this NpgsqlCommand command)
        => QueryAsync(command, CommandBehavior.Default);

    public static async Task<NpgsqlDataReader> QueryAsync(this NpgsqlConnection connection, NpgsqlCommand command, CommandBehavior commandBehavior)
    {
        await connection.OpenIfClosedAsync();
        return await command.ExecuteReaderAsync(commandBehavior);
    }

    public static async Task<NpgsqlDataReader> QueryAsync(this NpgsqlCommand command, CommandBehavior commandBehavior)
    {
        return await command.ExecuteReaderAsync(commandBehavior);
    }

    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> enumerable, int? initialCapacity = null)
    {
        var list = initialCapacity.HasValue ? new List<T>(initialCapacity.Value) : new List<T>();

        await foreach (var item in enumerable)
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

    private static NpgsqlCommand CreateCommand(this NpgsqlConnection connection, string commandText, params NpgsqlParameter[] parameters) =>
        ConfigureCommand(connection.CreateCommand(commandText), parameters);

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

    private static NpgsqlCommand CreateCommand(this NpgsqlConnection connection, string commandText, Action<NpgsqlParameterCollection>? configureParameters = null) =>
        ConfigureCommand(connection.CreateCommand(commandText), configureParameters);

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