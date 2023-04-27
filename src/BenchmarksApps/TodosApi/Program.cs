using Microsoft.Extensions.Options;
#if ENABLE_OPENAPI
using Microsoft.OpenApi.Models;
#endif
using Npgsql;
using TodosApi;

var builder = WebApplication.CreateSlimBuilder(args);

#if !ENABLE_LOGGING
builder.Logging.ClearProviders();
#endif

// Bind app settings from configuration & validate
builder.Services.ConfigureAppSettings(builder.Configuration);

// Configure authentication & authorization
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.ConfigureOptions<JwtConfiguration>();
builder.Services.AddAuthorization();

// Configure data access
builder.Services.AddSingleton(sp =>
{
    var appSettings = sp.GetRequiredService<IOptions<AppSettings>>().Value;
    return appSettings.GeneratingOpenApiDoc
        ? default!
        : new NpgsqlSlimDataSourceBuilder(appSettings.ConnectionString).Build();
});
builder.Services.AddHostedService<DatabaseInitializer>();

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

#if ENABLE_OPENAPI
// Configure OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Todos API", Version = "v1" });
});
#endif

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.MapShortCircuit(StatusCodes.Status404NotFound, "/favicon.ico");

app.MapHealthChecks("/health");

// Enables testing request exception handling behavior
app.MapGet("/throw", void () => throw new InvalidOperationException("You hit the throw endpoint"));

app.MapTodoApi();

#if !ENABLE_LOGGING
app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started. Press Ctrl+C to shut down."));
app.Lifetime.ApplicationStopping.Register(() => Console.WriteLine("Application is shutting down..."));
#endif

app.Run();
