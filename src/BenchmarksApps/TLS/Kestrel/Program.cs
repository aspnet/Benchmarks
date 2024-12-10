using System;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();

var config = new ConfigurationBuilder()
    .AddEnvironmentVariables(prefix: "ASPNETCORE_")
    .AddCommandLine(args)
    .Build();

builder.WebHost.UseKestrel(options =>
{
    var urls = config["urls"] ?? "https://localhost:5000/";
    foreach (var value in urls.Split([';'], StringSplitOptions.RemoveEmptyEntries))
    {
        Listen(options, config, value);
    }

    void Listen(KestrelServerOptions options, IConfigurationRoot config, string url)
    {
        var urlPrefix = UrlPrefix.Create(url);
        var endpoint = CreateIPEndPoint(urlPrefix);

        options.Listen(endpoint, listenOptions =>
        {
            // configure protocols
            var protocol = config["protocol"] ?? "";
            if (protocol.Equals("h2", StringComparison.OrdinalIgnoreCase))
            {
                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            }
            else if (protocol.Equals("h2c", StringComparison.OrdinalIgnoreCase))
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            }

            listenOptions.UseHttps("testCert.pfx", "testPassword");
        });
    }
});

var app = builder.Build();

app.MapGet("/hello-world", () =>
{
    return Results.Ok("Hello World!");
});

await app.StartAsync();
Console.WriteLine("Application started.");
await app.WaitForShutdownAsync();

static IPEndPoint CreateIPEndPoint(UrlPrefix urlPrefix)
{
    IPAddress ip;

    if (string.Equals(urlPrefix.Host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        ip = IPAddress.Loopback;
    }
    else if (!IPAddress.TryParse(urlPrefix.Host, out ip))
    {
        ip = IPAddress.IPv6Any;
    }

    return new IPEndPoint(ip, urlPrefix.PortValue);
}