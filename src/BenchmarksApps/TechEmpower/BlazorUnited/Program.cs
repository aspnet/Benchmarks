using BlazorUnited;
using BlazorUnited.Components;
using BlazorUnited.Database;
using Microsoft.AspNetCore.Components.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Disable logging as this is not required for the benchmark
#if RELEASE
builder.Logging.ClearProviders();
#endif

// Load custom configuration
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);

// Add services to the container.
builder.Services.AddSingleton(new Db(appSettings));
builder.Services.AddRazorComponents();

var app = builder.Build();

app.MapGet("/fortunes", () => new RazorComponentResult<Fortunes>());

app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started. Press Ctrl+C to shut down."));
app.Lifetime.ApplicationStopping.Register(() => Console.WriteLine("Application is shutting down..."));

app.Run();

// TODO: Blazor doesn't let you set the HtmlEncoder instance today, need to fix that.
//static HtmlEncoder CreateHtmlEncoder()
//{
//    var settings = new TextEncoderSettings(UnicodeRanges.BasicLatin, UnicodeRanges.Katakana, UnicodeRanges.Hiragana);
//    settings.AllowCharacter('\u2014'); // allow EM DASH through
//    return HtmlEncoder.Create(settings);
//}
