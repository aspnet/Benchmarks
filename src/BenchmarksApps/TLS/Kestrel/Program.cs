using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();

var writeCertValidationEventsToConsole = bool.TryParse(builder.Configuration["certValidationConsoleEnabled"], out var certValidationConsoleEnabled) && certValidationConsoleEnabled;
var mTlsEnabled = bool.TryParse(builder.Configuration["mTLS"], out var mTlsEnabledConfig) && mTlsEnabledConfig;
var tlsRenegotiationEnabled = bool.TryParse(builder.Configuration["tlsRenegotiation"], out var tlsRenegotiationEnabledConfig) && tlsRenegotiationEnabledConfig;
var statsEnabled = bool.TryParse(builder.Configuration["statsEnabled"], out var connectionStatsEnabledConfig) && connectionStatsEnabledConfig;
var listeningEndpoints = builder.Configuration["urls"] ?? "https://localhost:5000/";
var supportedTlsVersions = ParseSslProtocols(builder.Configuration["tlsProtocols"]);

if (mTlsEnabled && tlsRenegotiationEnabled)
{
    Console.WriteLine("mTLS and tlsRenegotiation require different clientCertMode setup. Using TLS Renegotiation by default.");
}

var connectionIds = new HashSet<string>();
var fetchedCertsCounter = 0;

builder.WebHost.UseKestrel(options =>
{
    foreach (var value in listeningEndpoints.Split([';'], StringSplitOptions.RemoveEmptyEntries))
    {
        ConfigureListen(options, builder.Configuration, value);
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
                options.SslProtocols = supportedTlsVersions;

                if (mTlsEnabled)
                {
                    options.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    options.ClientCertificateValidation = AllowAnyCertificateValidationWithLogging;
                }

                if (tlsRenegotiationEnabled)
                {
                    options.ClientCertificateMode = ClientCertificateMode.DelayCertificate;
                    options.ClientCertificateValidation = AllowAnyCertificateValidationWithLogging;
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

var app = builder.Build();

bool AllowAnyCertificateValidationWithLogging(X509Certificate2 certificate, X509Chain? chain, SslPolicyErrors errors)
{
    fetchedCertsCounter++;
    if (writeCertValidationEventsToConsole)
    {
        Console.WriteLine($"Certificate validation: {certificate.Subject} {certificate.Thumbprint}");
    }

    // Not interested in measuring actual certificate validation code:
    // we only need to measure the work of getting to the point where certificate is accessible and can be validated
    return true;
}

if (statsEnabled)
{
    Console.WriteLine("Registered stats middleware");
    app.Use(async (context, next) =>
    {
        connectionIds.Add(context.Connection.Id);
        Console.WriteLine($"[stats] unique connections established: {connectionIds.Count}; fetched certificates: {fetchedCertsCounter}");

        await next();
    });
}

if (tlsRenegotiationEnabled)
{
    Console.WriteLine("Registered TLS renegotiation middleware");
    app.Use(async (context, next) =>
    {
        var clientCert = context.Connection.ClientCertificate;
        if (clientCert is null)
        {
            Console.WriteLine($"No client certificate provided. Fetching for connection {context.Connection.Id}");
            clientCert = await context.Connection.GetClientCertificateAsync();
        }
        else
        {
            Console.WriteLine($"client certificate ({clientCert.Thumbprint}) already exists on the connection {context.Connection.Id}");
        }

        await next();
    });
}

app.MapGet("/hello-world", () =>
{
    return Results.Ok("Hello World!");
});

await app.StartAsync();

Console.WriteLine("Application Info:");
if (mTlsEnabled)
{
    Console.WriteLine($"\tmTLS is enabled (client cert is required)");
}
if (tlsRenegotiationEnabled)
{
    Console.WriteLine($"\tlsRenegotiationEnabled is enabled (client cert is allowed)");
}
if (writeCertValidationEventsToConsole)
{
    Console.WriteLine($"\tenabled logging certificate validation events to console");
}
if (statsEnabled)
{
    Console.WriteLine($"\tenabled logging stats to console");
}
Console.WriteLine($"\tsupported TLS versions: {supportedTlsVersions}");
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

static SslProtocols ParseSslProtocols(string? supportedTlsVersions)
{
    var protocols = SslProtocols.Tls12; // default it TLS 1.2
    if (string.IsNullOrEmpty(supportedTlsVersions))
    {
        return protocols;
    }

    protocols = SslProtocols.None;
    foreach (var version in supportedTlsVersions.Split(','))
    {
        switch (version.Trim().ToLower())
        {
#pragma warning disable SYSLIB0039 // Type or member is obsolete
            case "tls11":
                protocols |= SslProtocols.Tls11;
                break;
#pragma warning restore SYSLIB0039 // Type or member is obsolete
            case "tls12":
                protocols |= SslProtocols.Tls12;
                break;
            case "tls13":
                protocols |= SslProtocols.Tls13;
                break;
            default:
                throw new ArgumentException($"Unsupported TLS version: {version}");
        }
    }

    return protocols;
}