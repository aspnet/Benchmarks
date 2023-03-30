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
                      {nameof(Todo.DueBy)} date NULL,
                      {nameof(Todo.IsComplete)} boolean NOT NULL DEFAULT false
                  );
                  ALTER TABLE IF EXISTS public.todos
                      OWNER to "TodosApp";
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
