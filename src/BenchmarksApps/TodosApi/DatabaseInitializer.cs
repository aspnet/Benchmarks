using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using Npgsql;

namespace TodosApi;

internal class DatabaseInitializer : IHostedService
{
    private readonly NpgsqlDataSource _db;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly bool _initDatabase;

    public DatabaseInitializer(NpgsqlDataSource db, IOptions<AppSettings> appSettings, IServer server, ILogger<DatabaseInitializer> logger)
    {
        _db = db;
        _logger = logger;
        _initDatabase = !appSettings.Value.SuppressDbInitialization
            // Only run if this is an actual IServer implementation with addresses to listen on.
            // Will not be the case for TestServer, NoopServer injected by the OpenAPI doc generator tool, etc.
            && server.Features.Get<IServerAddressesFeature>() is { Addresses.Count: >0 };
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_initDatabase)
        {
            return Initialize(cancellationToken);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Database initialization disabled for connection string '{connectionString}'", _db?.ConnectionString);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task Initialize(CancellationToken cancellationToken = default)
    {
        // NOTE: Npgsql removes the password from the connection string
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Ensuring database exists and is up to date at connection string '{connectionString}'", _db.ConnectionString);
        }

        var sql = $"""
                CREATE TABLE IF NOT EXISTS public.todos
                (
                    {nameof(Todo.Id)} SERIAL PRIMARY KEY,
                    {nameof(Todo.Title)} text NOT NULL,
                    {nameof(Todo.DueBy)} date NULL,
                    {nameof(Todo.IsComplete)} boolean NOT NULL DEFAULT false
                );
                DELETE FROM public.todos;
                INSERT INTO
                    public.todos ({nameof(Todo.Title)}, {nameof(Todo.DueBy)}, {nameof(Todo.IsComplete)})
                VALUES
                    ('Wash the dishes.', CURRENT_DATE, true),
                    ('Dry the dishes.', CURRENT_DATE, true),
                    ('Turn the dishes over.', CURRENT_DATE, false),
                    ('Walk the kangaroo.', CURRENT_DATE + INTERVAL '1 day', false),
                    ('Call Grandma.', CURRENT_DATE + INTERVAL '1 day', false);
                """;
        await _db.ExecuteAsync(sql, cancellationToken);
    }
}
