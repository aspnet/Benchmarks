using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();

var config = new ConfigurationBuilder()
    .AddEnvironmentVariables(prefix: "ASPNETCORE_")
    .AddCommandLine(args)
    .AddJsonFile("appsettings.json")
#if DEBUG
    .AddJsonFile($"appsettings.Development.json")
#endif
    .Build();

var writeCertValidationEventsToConsole = bool.TryParse(config["certValidationConsoleEnabled"], out var certValidationConsoleEnabled) && certValidationConsoleEnabled;
var mTlsEnabled = bool.TryParse(config["mTLS"], out var mTlsEnabledConfig) && mTlsEnabledConfig;
var listeningEndpoints = config["urls"] ?? "https://localhost:5000/";

builder.WebHost.UseKestrel(options =>
{
    foreach (var value in listeningEndpoints.Split([';'], StringSplitOptions.RemoveEmptyEntries))
    {
        ConfigureListen(options, config, value);
    }

    void ConfigureListen(KestrelServerOptions serverOptions, IConfigurationRoot config, string url)
    {
        var urlPrefix = UrlPrefix.Create(url);
        var endpoint = CreateIPEndPoint(urlPrefix);

        serverOptions.Listen(endpoint, listenOptions =>
        {
            // [SuppressMessage("Microsoft.Security", "CSCAN0220.DefaultPasswordContexts", Justification="Benchmark code, not a secret")]
            listenOptions.UseHttps("testCert.pfx", "testPassword", options =>
            {
                if (mTlsEnabled)
                {
                    options.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    options.ClientCertificateValidation = writeCertValidationEventsToConsole ? AllowAnyCertificateValidationWithLogging : AllowAnyCertificateValidation;
                }
            });

            var protocol = config["protocol"] ?? "";
            if (protocol.Equals("h2", StringComparison.OrdinalIgnoreCase))
            {
                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            }
            else if (protocol.Equals("h2c", StringComparison.OrdinalIgnoreCase))
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            }
        });
    }
});

bool AllowAnyCertificateValidationWithLogging(X509Certificate2 certificate, X509Chain? chain, SslPolicyErrors errors)
{
    if (writeCertValidationEventsToConsole)
    {
        Console.WriteLine($"Certificate validation: {certificate.Subject} {certificate.Thumbprint}");
    }

    return AllowAnyCertificateValidation(certificate, chain, errors);
}

static bool AllowAnyCertificateValidation(X509Certificate2 certificate, X509Chain? chain, SslPolicyErrors errors)
{
    // Not interested in measuring actual certificate validation code:
    // we only need to measure the work of getting to the point where certificate is accessible and can be validated
    return true;
}

var app = builder.Build();

app.MapGet("/hello-world", () =>
{
    return Results.Ok("Hello World!");
});

await app.StartAsync();

Console.WriteLine("Application Info:");
if (mTlsEnabled) Console.WriteLine($"\tmTLS is enabled (client cert is required)");
if (writeCertValidationEventsToConsole) Console.WriteLine($"\tenabled logging certificate validation events to console");
Console.WriteLine($"\tlistening endpoints: {listeningEndpoints}");
Console.WriteLine("--------------------------------");

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