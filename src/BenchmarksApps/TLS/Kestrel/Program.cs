using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

Console.WriteLine("Starting application...");

var builder = WebApplication.CreateBuilder(args);
// builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Debug).AddConsole();

// behavioral
var mTlsEnabled = bool.TryParse(builder.Configuration["mTLS"], out var mTlsEnabledConfig) && mTlsEnabledConfig;
var tlsRenegotiationEnabled = bool.TryParse(builder.Configuration["tlsRenegotiation"], out var tlsRenegotiationEnabledConfig) && tlsRenegotiationEnabledConfig;
var certPublicKeySpecified = int.TryParse(builder.Configuration["certPublicKeyLength"], out var certPublicKeyConfig);
var certPublicKeyLength = certPublicKeySpecified ? certPublicKeyConfig : 2048;
var enableHostHeaderValidation = bool.TryParse(builder.Configuration["enableHostHeaderValidation"], out var enableHostHeaderValidationConfig) && enableHostHeaderValidationConfig;

// endpoints
var listeningEndpoints = builder.Configuration["urls"] ?? "https://localhost:5000/";
var supportedTlsVersions = ParseSslProtocols(builder.Configuration["tlsProtocols"]);

// debug
var writeCertValidationEventsToConsole = bool.TryParse(builder.Configuration["certValidationConsoleEnabled"], out var certValidationConsoleEnabled) && certValidationConsoleEnabled;
var statsEnabled = bool.TryParse(builder.Configuration["statsEnabled"], out var connectionStatsEnabledConfig) && connectionStatsEnabledConfig;
var logRequestDetails = bool.TryParse(builder.Configuration["logRequestDetails"], out var logRequestDetailsConfig) && logRequestDetailsConfig;

if (mTlsEnabled && tlsRenegotiationEnabled)
{
    Console.WriteLine("mTLS and tlsRenegotiation require different clientCertMode setup. Using TLS Renegotiation by default.");
}

var connectionIds = new HashSet<string>();
var fetchedCertsCounter = 0;

if (enableHostHeaderValidation)
{
    builder.Services.Configure<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>(options =>
    {
        var allowedHosts = new HashSet<string>();
        foreach (var endpoint in listeningEndpoints.Split([';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var urlPrefix = UrlPrefix.Create(endpoint);
            allowedHosts.Add(urlPrefix.Host);
        }

        Console.WriteLine("Configured HostFilteringOptions. Hosts: " + string.Join(';', allowedHosts));
        options.AllowedHosts = allowedHosts.ToArray();
        options.IncludeFailureMessage = true; // Suppress the failure message in response body
        options.AllowEmptyHosts = true;
    });
}

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
            var certificatePath = Path.Combine("certificates", $"testCert-{certPublicKeyLength}.pfx");
            Console.WriteLine($"Using certificate: {certificatePath}");

            var certPath =
#if DEBUG
            Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, certificatePath); // exe location
#else
            certificatePath;
#endif

            // for debug
            listenOptions.UseConnectionLogging();

            // [SuppressMessage("Microsoft.Security", "CSCAN0220.DefaultPasswordContexts", Justification="Benchmark code, not a secret")]
            listenOptions.UseHttps(certPath, "testPassword", options =>
            {
                if (supportedTlsVersions is not null)
                {
                    options.SslProtocols = supportedTlsVersions.Value;
                }

                // forcefully disable TLS resumption
                options.OnAuthenticate = (connectionContext, sslServerAuthOptions) =>
                {
                    sslServerAuthOptions.AllowTlsResume = false;
                };

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

if (enableHostHeaderValidation)
{
    Console.WriteLine("Enabled host header filtering middleware.");
    app.UseHostFiltering();
}

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

if (logRequestDetails)
{
    var logged = false;
    Console.WriteLine("Registered request details logging middleware");
    app.Use(async (context, next) =>
    {
        if (!logged)
        {
            logged = true;

            var tlsHandshakeFeature = context.Features.GetRequiredFeature<ITlsHandshakeFeature>();

            Console.WriteLine("Request details:");
            Console.WriteLine("-----");
            Console.WriteLine("TLS: " + tlsHandshakeFeature.Protocol);
            Console.WriteLine("-----");
        }

        await next(context);
    });
}

if (statsEnabled)
{
    Console.WriteLine("Registered stats middleware");
    app.Use(async (context, next) =>
    {
        connectionIds.Add(context.Connection.Id);
        Console.WriteLine($"[stats] unique connections established: {connectionIds.Count}; fetched certificates: {fetchedCertsCounter}");

        await next(context);
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

        await next(context);
    });
}

app.MapGet("/hello-world", () =>
{
    return Results.Ok("Hello World!");
});

await app.StartAsync();

Console.WriteLine("Application Info:");
LogOpenSSLVersion();
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

static SslProtocols? ParseSslProtocols(string? supportedTlsVersions)
{
    var protocols = SslProtocols.None;
    if (string.IsNullOrEmpty(supportedTlsVersions) || supportedTlsVersions == "any")
    {
        return null;
    }

    foreach (var version in supportedTlsVersions.Split(','))
    {
        switch (version.Trim().ToLower())
        {
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

static void LogOpenSSLVersion()
{
    if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
    {
        return;
    }

    using var process = new Process()
    {
        StartInfo =
        {
            FileName = "/usr/bin/env",
            Arguments = "openssl version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        },
    };

    process.Start();
    process.WaitForExit();
    var output = process.StandardOutput.ReadToEnd();
    Console.WriteLine(output);
}