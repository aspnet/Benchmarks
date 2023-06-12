using TodosApi;

var builder = WebApplication.CreateSlimBuilder(args);

#if !ENABLE_LOGGING
builder.Logging.ClearProviders();
#endif

// Bind app settings from configuration & validate
builder.Services.ConfigureAppSettings(builder.Configuration, builder.Environment);

// Configure authentication & authorization
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.ConfigureOptions<JwtConfiguration>();
builder.Services.AddAuthorization();

// Configure data access
builder.Services.AddDatabase();

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

// OpenAPI
builder.Services.AddOpenApi();

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
app.RegisterStartup();
app.Lifetime.ApplicationStopping.Register(() => Console.WriteLine("Application is shutting down..."));
#endif

app.Run();
