using Minimal;
using Minimal.Database;
using Minimal.Models;
using Minimal.Templates;

var builder = WebApplication.CreateBuilder(args);

// Remove logging as this is not required for the benchmark
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
builder.Services.AddFluid();

var app = builder.Build();

app.MapGet("/plaintext", () => "Hello, World!");

app.MapGet("/json", () => new { message = "Hello, World!" });

app.MapGet("/fortunes2", async (HttpContext context, Db db) => {
    var fortunes = await db.LoadFortunesRows();
    await FortunesTemplate.Render(fortunes, context.Response);
});

app.MapGet("/fortunes", async (HttpContext context, Db db) => {
    var fortunes = await db.LoadFortunesRows();
    return Results.Extensions.View("fortunes", new ViewModel { Fortunes = fortunes });
});

app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started. Press Ctrl+C to shut down."));
app.Lifetime.ApplicationStopping.Register(() => Console.WriteLine("Application is shutting down..."));

app.Run();
