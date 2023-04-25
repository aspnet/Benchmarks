using Npgsql;
using TodosApi;

var builder = WebApplication.CreateSlimBuilder(args);

#if !ENABLE_LOGGING
builder.Logging.ClearProviders();
#endif

// Configure authentication & authorization
builder.Services.AddAuthentication()
    .AddJwtBearer(JwtConfiguration.ConfigureJwtBearer(builder));

builder.Services.AddAuthorization();

// Configure data access
var connectionString = builder.Configuration.GetConnectionString("TodoDb")
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
    .AddCheck<DatabaseHealthCheck>("Database", timeout: TimeSpan.FromSeconds(2))
    .AddCheck<JwtHealthCheck>("JwtAuthentication");

// Problem details
builder.Services.AddProblemDetails();

var app = builder.Build();

await Database.Initialize(app.Services, app.Logger);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.MapHealthChecks("/health");
// Enables testing request exception handling behavior
app.MapGet("/throw", void () => throw new InvalidOperationException("You hit the throw endpoint"));

app.MapTodoApi();

#if !ENABLE_LOGGING
app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started. Press Ctrl+C to shut down."));
app.Lifetime.ApplicationStopping.Register(() => Console.WriteLine("Application is shutting down..."));
#endif

app.Run();
