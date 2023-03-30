using Microsoft.Extensions.Logging.Configuration;
using Npgsql;
using TodosApi;

var builder = WebApplication.CreateSlimBuilder(args);

// Load custom configuration
var settingsFiles = new[] { "appsettings.json", $"appsettings.{builder.Environment.EnvironmentName}.json" };
foreach (var settingsFile in settingsFiles)
{
    builder.Configuration.AddJsonFile(builder.Environment.ContentRootFileProvider, settingsFile, optional: false, reloadOnChange: true);
}
#if DEBUG || DEBUG_DATABASE
builder.Configuration.AddUserSecrets<Program>();
#endif

// Configure logging
builder.Logging
    .AddConfiguration(builder.Configuration)
    .AddSimpleConsole();

// Configure authentication & authorization
builder.Services.AddAuthentication()
    .AddJwtBearer(JwtConfiguration.ConfigureJwtBearer(builder));

builder.Services.AddAuthorization();

// Configure data access
var connectionString = builder.Configuration.GetConnectionString("TodoDb")
    ?? builder.Configuration["CONNECTION_STRING"]
    ?? throw new InvalidOperationException("Connection string not found. If running locally, set the connection string in user secrets for key 'ConnectionStrings:TodoDb'.");
builder.Services.AddSingleton(_ => new NpgsqlSlimDataSourceBuilder(connectionString).Build());

// Configure JSON serialization
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, JwtOptionsJsonSerializerContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, TodoApiJsonSerializerContext.Default);
});

var app = builder.Build();

await Database.Initialize(app.Services, app.Logger);

app.MapTodoApi();

app.Run();
