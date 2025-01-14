using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.HttpSys;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();

var writeCertValidationEventsToConsole = bool.TryParse(builder.Configuration["certValidationConsoleEnabled"], out var certValidationConsoleEnabled) && certValidationConsoleEnabled;
var statsEnabled = bool.TryParse(builder.Configuration["statsEnabled"], out var connectionStatsEnabledConfig) && connectionStatsEnabledConfig;
var mTlsEnabled = bool.TryParse(builder.Configuration["mTLS"], out var mTlsEnabledConfig) && mTlsEnabledConfig;
var tlsRenegotiationEnabled = bool.TryParse(builder.Configuration["tlsRenegotiation"], out var tlsRenegotiationEnabledConfig) && tlsRenegotiationEnabledConfig;
var listeningEndpoints = builder.Configuration["urls"] ?? "https://localhost:5000/";

#pragma warning disable CA1416 // Can be launched only on Windows (HttpSys)
builder.WebHost.UseHttpSys(options =>
{
    // meaning client can send a certificate, but it can be explicitly requested by server as well (renegotiation)
    options.ClientCertificateMethod = ClientCertificateMethod.AllowRenegotation;
});
#pragma warning restore CA1416 // Can be launched only on Windows (HttpSys)

var app = builder.Build();  

app.MapGet("/hello-world", () =>
{
    return Results.Ok("Hello World!");
});

var connectionIds = new HashSet<string>();
var fetchedCertsCounter = 0;

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

if (mTlsEnabled)
{
    try
    {
        ConfigureHttpSysForMutualTls();
    }
    catch (Exception ex)
    {
        throw new Exception($"Http.Sys configuration for mTLS failed. Current dir: {Directory.GetCurrentDirectory()}", innerException: ex);
    }
}

if (tlsRenegotiationEnabled)
{
    // this is an http.sys middleware to get a cert
    Console.WriteLine("Registered client cert validation middleware");

    app.Use(async (context, next) => {
        var clientCert = context.Connection.ClientCertificate;
        if (clientCert is null)
        {
            if (writeCertValidationEventsToConsole)
            {
                Console.WriteLine($"No client certificate provided. Fetching for connection {context.Connection.Id}");
            }

            clientCert = await context.Connection.GetClientCertificateAsync(CancellationToken.None);
            Interlocked.Increment(ref fetchedCertsCounter);
        }
        else
        {
            if (writeCertValidationEventsToConsole)
            {
                Console.WriteLine($"client certificate ({clientCert.Thumbprint}) already exists on the connection {context.Connection.Id}");
            }
        }

        // we have a client cert here, and lets imagine we do the validation here
        // if (clientCert.Thumbprint != "1234567890") throw new NotImplementedException();

        await next();
    });
}

await app.StartAsync();
Console.WriteLine("Application Info:");
if (mTlsEnabled)
{
    Console.WriteLine($"\tmTLS is enabled (client cert is required)");
}
if (writeCertValidationEventsToConsole)
{
    Console.WriteLine($"\tenabled logging certificate validation events to console");
}
if (statsEnabled)
{
    Console.WriteLine($"\tenabled logging stats to console");
}
Console.WriteLine($"\tlistening endpoints: {listeningEndpoints}");
Console.WriteLine("--------------------------------");

Console.WriteLine("Application started.");
await app.WaitForShutdownAsync();

void ConfigureHttpSysForMutualTls()
{
    Console.WriteLine("Setting up mTLS for http.sys");

    var certificate = new X509Certificate2("testCert.pfx", "testPassword", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
    using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
    {
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
        store.Close();
    }

    string certThumbprint = certificate.Thumbprint;
    string appId = Guid.NewGuid().ToString();

    string command = $"http add sslcert ipport=0.0.0.0:5000 certhash={certThumbprint} appid={{{appId}}} clientcertnegotiation=enable";
    ProcessStartInfo processInfo = new ProcessStartInfo("netsh", command)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using Process process = Process.Start(processInfo)!;
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Failed to configure http.sys: {error}");
    }

    Console.WriteLine("Configured http.sys settings for mTLS");
}