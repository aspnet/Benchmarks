using Minimal;
using Minimal.Database;
using Minimal.Templates;

var builder = WebApplication.CreateBuilder(args);

// Disable logging as this is not required for the benchmark
builder.Logging.ClearProviders();

builder.WebHost.ConfigureKestrel(options =>
{
     options.AllowSynchronousIO = true;
});

// Load custom configuration
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);

// Add services to the container.
builder.Services.AddSingleton(new Db(appSettings));

var app = builder.Build();

app.MapGet("/plaintext", () => "Hello, World!");

app.MapGet("/json", () => new { message = "Hello, World!" });

app.MapGet("/db", async (Db db) => await db.LoadSingleQueryRow());

app.MapGet("/fortunes", async (HttpContext context, Db db) => {
    var fortunes = await db.LoadFortunesRows();
    await FortunesTemplate.Render(fortunes, context.Response);
});

app.MapGet("/queries/{count}", async (Db db, int count) => await db.LoadMultipleQueriesRows(count));

app.MapGet("/updates/{count}", async (Db db, int count) => await db.LoadMultipleUpdatesRows(count));

app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started. Press Ctrl+C to shut down."));
app.Lifetime.ApplicationStopping.Register(() => Console.WriteLine("Application is shutting down..."));

app.Run();
