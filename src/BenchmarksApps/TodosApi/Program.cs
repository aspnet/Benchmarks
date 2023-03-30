using Microsoft.Extensions.Logging.Configuration;
using Npgsql;
using TodosApi;

var builder = WebApplication.CreateSlimBuilder(args);

#if ENABLE_LOGGING
// Load custom configuration
var settingsFiles = new[] { "appsettings.json", $"appsettings.{builder.Environment.EnvironmentName}.json" };
foreach (var settingsFile in settingsFiles)
{
    builder.Configuration.AddJsonFile(builder.Environment.ContentRootFileProvider, settingsFile, optional: true, reloadOnChange: true);
}
builder.Configuration.AddUserSecrets<Program>();

// Configure logging
builder.Logging
    .AddConfiguration(builder.Configuration.GetSection("Logging"))
    .AddSimpleConsole();
#endif

// Configure authentication & authorization
builder.Services.AddAuthentication()
    .AddJwtBearer(JwtConfiguration.ConfigureJwtBearer(builder));

builder.Services.AddAuthorization();

// Configure data access
var connectionString = builder.Configuration.GetConnectionString("TodoDb")
    ?? builder.Configuration["CONNECTION_STRING"]
    ?? throw new InvalidOperationException("""
        Connection string not found.
        If running locally, set the connection string in user secrets for key 'ConnectionStrings:TodoDb'.
        If running after deployment, set the connection string via the environment variable 'CONNECTIONSTRINGS__TODODB'.
        """);
builder.Services.AddSingleton(_ => new NpgsqlSlimDataSourceBuilder(connectionString).Build());

// Configure JSON serialization
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, TodoApiJsonSerializerContext.Default);
});

// Configure health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("Database")
    .AddCheck<JwtHealthCheck>("JwtAuthentication");

var app = builder.Build();

await Database.Initialize(app.Services, app.Logger);

app.MapHealthChecks("/health");

app.MapTodoApi();

#if !ENABLE_LOGGING
app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started. Press Ctrl+C to shut down."));
app.Lifetime.ApplicationStopping.Register(() => Console.WriteLine("Application is shutting down..."));
#endif

app.Run();
