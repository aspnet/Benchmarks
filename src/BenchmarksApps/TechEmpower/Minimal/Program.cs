using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Http.HttpResults;
using Minimal;
using Minimal.Database;
using Minimal.Models;
using Minimal.Templates;

var builder = WebApplication.CreateBuilder(args);

// Disable logging as this is not required for the benchmark
builder.Logging.ClearProviders();

// Load custom configuration
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);

// Add services to the container.
builder.Services.AddSingleton(new Db(appSettings));
builder.Services.AddSingleton(CreateHtmlEncoder());

var app = builder.Build();

app.MapGet("/plaintext", () => "Hello, World!");

app.MapGet("/plaintext/result", () => Results.Text("Hello, World!"));

app.MapGet("/json", () => new { message = "Hello, World!" });

app.MapGet("/json/result", () => Results.Json(new { message = "Hello, World!" }));

app.MapGet("/db", async (Db db) => await db.LoadSingleQueryRow());

app.MapGet("/db/result", async (Db db) => Results.Json(await db.LoadSingleQueryRow()));

app.MapGet("/fortunes", async (HttpContext context, Db db, HtmlEncoder htmlEncoder) => {
    var fortunes = await db.LoadFortunesRows();
    //var fortunes = await db.LoadFortunesRowsNoDb(); // Don't call the database
    var template = (RazorSliceHttpResult<List<Fortune>>)Fortunes.Create(fortunes);
    template.HtmlEncoder = htmlEncoder;
    return template;
});

app.MapGet("/queries/{count}", async (Db db, int count) => await db.LoadMultipleQueriesRows(count));

app.MapGet("/queries/{count}/result", async (Db db, int count) => Results.Json(await db.LoadMultipleQueriesRows(count)));

app.MapGet("/updates/{count}", async (Db db, int count) => await db.LoadMultipleUpdatesRows(count));

app.MapGet("/updates/{count}/result", async (Db db, int count) => Results.Json(await db.LoadMultipleUpdatesRows(count)));

app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started. Press Ctrl+C to shut down."));
app.Lifetime.ApplicationStopping.Register(() => Console.WriteLine("Application is shutting down..."));

app.Run();

static HtmlEncoder CreateHtmlEncoder()
{
    var settings = new TextEncoderSettings(UnicodeRanges.BasicLatin, UnicodeRanges.Katakana, UnicodeRanges.Hiragana);
    settings.AllowCharacter('\u2014'); // allow EM DASH through
    return HtmlEncoder.Create(settings);
}
