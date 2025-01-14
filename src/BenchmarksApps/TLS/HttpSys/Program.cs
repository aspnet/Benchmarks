using HttpSys;
using Microsoft.AspNetCore.Server.HttpSys;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();

var writeCertValidationEventsToConsole = bool.TryParse(builder.Configuration["certValidationConsoleEnabled"], out var certValidationConsoleEnabled) && certValidationConsoleEnabled;
var httpSysLoggingEnabled = bool.TryParse(builder.Configuration["httpSysLogs"], out var httpSysLogsEnabled) && httpSysLogsEnabled;
var statsEnabled = bool.TryParse(builder.Configuration["statsEnabled"], out var connectionStatsEnabledConfig) && connectionStatsEnabledConfig;
var mTlsEnabled = bool.TryParse(builder.Configuration["mTLS"], out var mTlsEnabledConfig) && mTlsEnabledConfig;
var tlsRenegotiationEnabled = bool.TryParse(builder.Configuration["tlsRenegotiation"], out var tlsRenegotiationEnabledConfig) && tlsRenegotiationEnabledConfig;
var listeningEndpoints = builder.Configuration["urls"] ?? "https://localhost:5000/";
var httpsIpPort = listeningEndpoints.Split(";").First(x => x.Contains("https")).Replace("https://", "");

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
    var hostAppLifetime = app.Services.GetService<IHostApplicationLifetime>();
    hostAppLifetime!.ApplicationStopping.Register(OnShutdown);

    void OnShutdown()
    {
        Console.WriteLine("Application shutdown started.");

        try
        {
            NetShWrapper.DisableHttpSysMutualTls(ipPort: httpsIpPort);
        }
        catch
        {
            Console.WriteLine("Failed to disable HTTP.SYS mTLS settings");
            throw;
        }
    }

    try
    {
        NetShWrapper.EnableHttpSysMutualTls(ipPort: httpsIpPort);
    }
    catch
    {
        Console.WriteLine($"Http.Sys configuration for mTLS failed");
        throw;
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

if (httpSysLoggingEnabled)
{
    NetShWrapper.Show();
}

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