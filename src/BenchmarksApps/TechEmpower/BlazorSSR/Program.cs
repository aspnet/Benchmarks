using System.Text.Encodings.Web;
using System.Text.Unicode;
using BlazorSSR;
using BlazorSSR.Components;
using BlazorSSR.Database;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); // Disable logging as this is not required for the benchmark

bool useAntiforgery = true;
if (bool.TryParse(builder.Configuration["use-antiforgery"], out var useAntiforgeryConfig))
{
    useAntiforgery = useAntiforgeryConfig;
}

// Load custom configuration
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);

// Add services to the container.
builder.Services.AddDbContextPool<AppDbContext>(options => options
    .UseNpgsql(appSettings.ConnectionString, npgsql => npgsql.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d)))
    .EnableThreadSafetyChecks(false));

builder.Services.AddSingleton(new Db(appSettings));
builder.Services.AddRazorComponents();
builder.Services.AddSingleton(serviceProvider =>
{
    var settings = new TextEncoderSettings(UnicodeRanges.BasicLatin, UnicodeRanges.Katakana, UnicodeRanges.Hiragana);
    settings.AllowCharacter('\u2014'); // allow EM DASH through
    return HtmlEncoder.Create(settings);
});

Console.WriteLine("everything from builder.Configuration:");
foreach (var i in builder.Configuration.AsEnumerable())
{
    Console.WriteLine($"{i.Key}: '{i.Value}'");
}
Console.WriteLine("--------------------------------------");

var app = builder.Build();

if (useAntiforgery)
{
    Console.WriteLine("Antiforgery is enabled.");
    app.UseAntiforgery();
}

app.MapRazorComponents<App>();

app.MapGet("/direct/fortunes", () => new RazorComponentResult<Fortunes>());
app.MapGet("/direct/fortunes-ef", () => new RazorComponentResult<FortunesEf>());

app.MapGet("/direct/fortunes/params", async (HttpContext context, Db db) => {
    var fortunes = await db.LoadFortunesRowsDapper();
    //var fortunes = await db.LoadFortunesRowsNoDb(); // Don't call the database
    var parameters = new Dictionary<string, object?> { { nameof(FortunesParameters.Rows), fortunes } };
    //var parameters = new FortunesRazorParameters(fortunes); // Custom parameters class to avoid allocating a Dictionary
    var result = new RazorComponentResult<FortunesParameters>(parameters)
    {
        PreventStreamingRendering = true
    };
    return result;
});

app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started. Press Ctrl+C to shut down."));
app.Lifetime.ApplicationStopping.Register(() => Console.WriteLine("Application is shutting down..."));

app.Run();
