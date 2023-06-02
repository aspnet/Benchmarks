using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using BlazorSSR;
using BlazorSSR.Components;
using BlazorSSR.Database;
using System.Text.Encodings.Web;
using System.Text.Unicode;

var builder = WebApplication.CreateBuilder(args);

// Disable logging as this is not required for the benchmark
builder.Logging.ClearProviders();

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
    // TODO: This custom configured HtmlEncoder won't actually be used until Blazor supports it: https://github.com/dotnet/aspnetcore/issues/47477
    var settings = new TextEncoderSettings(UnicodeRanges.BasicLatin, UnicodeRanges.Katakana, UnicodeRanges.Hiragana);
    settings.AllowCharacter('\u2014'); // allow EM DASH through
    return HtmlEncoder.Create(settings);
});

var app = builder.Build();

app.MapRazorComponents<App>();

app.MapGet("/direct/fortunes", () => new RazorComponentResult<Fortunes>());
app.MapGet("/direct/fortunes-ef", () => new RazorComponentResult<FortunesEf>());

app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started. Press Ctrl+C to shut down."));
app.Lifetime.ApplicationStopping.Register(() => Console.WriteLine("Application is shutting down..."));

app.Run();
