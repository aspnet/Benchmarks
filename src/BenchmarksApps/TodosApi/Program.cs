using TodosApi;

var builder = WebApplication.CreateSlimBuilder(args);

#if !ENABLE_LOGGING
builder.Logging.ClearProviders();
#endif

// Add service defaults
builder.AddServiceDefaults();

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

// Problem details
builder.Services.AddProblemDetails();

// OpenAPI
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) => {
        document.Info.Title = "Todos API";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.MapShortCircuit(StatusCodes.Status404NotFound, "/favicon.ico");

app.MapDefaultEndpoints();

// Enables testing request exception handling behavior
app.MapGet("/throw", void () => throw new InvalidOperationException("You hit the throw endpoint"));

app.MapTodoApi();

#if !ENABLE_LOGGING
app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started. Press Ctrl+C to shut down."));
app.Lifetime.ApplicationStopping.Register(() => Console.WriteLine("Application is shutting down..."));
#endif

app.Run();
