using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Http.HttpResults;
using RazorSlices;
using Minimal;
using Minimal.Database;
using Minimal.Models;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);

// Disable logging as this is not required for the benchmark
builder.Logging.ClearProviders();

builder.WebHost.ConfigureKestrel(options =>
{
    options.AllowSynchronousIO = true;
});

// Allow multiple processes bind to the same port. This also "works" on Windows in that it will
// prevent address in use errors and hand off to another process if no others are available,
// but it wouldn't round-robin new connections between processes like it will on Linux.
if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    builder.WebHost.UseSockets(options =>
    {
        options.CreateBoundListenSocket = endpoint =>
        {
            if (endpoint is not IPEndPoint ip)
            {
                return SocketTransportOptions.CreateDefaultBoundListenSocket(endpoint);
            }

            // Normally, we'd call CreateDefaultBoundListenSocket for the IPEndpoint too, but we need
            // to set ReuseAddress before calling bind, and CreateDefaultBoundListenSocket calls bind.
            var listenSocket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            // Kestrel expects IPv6Any to bind to both IPv6 and IPv4
            if (ip.Address.Equals(IPAddress.IPv6Any))
            {
                listenSocket.DualMode = true;
            }

            listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listenSocket.Bind(ip);

            return listenSocket;
        };
    });
}

// Load custom configuration
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);

// Add services to the container.
builder.Services.AddSingleton(new Db(appSettings));

var app = builder.Build();

app.MapGet("/plaintext", () => "Hello, World!");

app.MapGet("/plaintext/result", () => Results.Text("Hello, World!"));

app.MapGet("/json", () => new { message = "Hello, World!" });

app.MapGet("/json/result", () => Results.Json(new { message = "Hello, World!" }));

app.MapGet("/db", async (Db db) => await db.LoadSingleQueryRow());

app.MapGet("/db/result", async (Db db) => Results.Json(await db.LoadSingleQueryRow()));

var createFortunesTemplate = RazorSlice.ResolveSliceFactory<List<Fortune>>("/Templates/Fortunes.cshtml");
var htmlEncoder = CreateHtmlEncoder();

app.MapGet("/fortunes", async (HttpContext context, Db db) =>
{
    var fortunes = await db.LoadFortunesRows();
    var template = (RazorSliceHttpResult<List<Fortune>>)createFortunesTemplate(fortunes);
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