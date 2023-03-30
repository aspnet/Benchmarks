using Npgsql;

namespace TodosApi;

internal static class Database
{
    public static async Task Initialize(IServiceProvider services, ILogger logger)
    {
        var db = services.GetRequiredService<NpgsqlDataSource>();

        if (Environment.GetEnvironmentVariable("SUPPRESS_DB_INIT") != "true")
        {
            logger.LogInformation("Ensuring database exists and is up to date at connection string '{connectionString}'", ObscurePassword(db.ConnectionString));

            var sql = $"""
                  CREATE TABLE IF NOT EXISTS public.todos
                  (
                      {nameof(Todo.Id)} SERIAL PRIMARY KEY,
                      {nameof(Todo.Title)} text NOT NULL,
                      {nameof(Todo.IsComplete)} boolean NOT NULL DEFAULT false
                  );
                  ALTER TABLE IF EXISTS public.todos
                      OWNER to "TodosApp";
                  DELETE FROM public.todos;
                  """;
            await db.ExecuteAsync(sql);
        }
        else
        {
            logger.LogInformation("Database initialization disabled for connection string '{connectionString}'", ObscurePassword(db.ConnectionString));
        }

        string ObscurePassword(string connectionString)
        {
            var passwordKey = "Password=";
            var passwordIndex = connectionString.IndexOf(passwordKey, 0, StringComparison.OrdinalIgnoreCase);
            if (passwordIndex < 0)
            {
                return connectionString;
            }
            var semiColonIndex = connectionString.IndexOf(";", passwordIndex, StringComparison.OrdinalIgnoreCase);
            return string.Concat(connectionString.AsSpan(0, passwordIndex + passwordKey.Length), "*****", semiColonIndex >= 0 ? connectionString[semiColonIndex..] : "");
        }
    }
}
