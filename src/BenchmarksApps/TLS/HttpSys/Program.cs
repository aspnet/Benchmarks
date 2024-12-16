using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.HttpSys;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();

var writeCertValidationEventsToConsole = bool.TryParse(builder.Configuration["certValidationConsoleEnabled"], out var certValidationConsoleEnabled) && certValidationConsoleEnabled;
var statsEnabled = bool.TryParse(builder.Configuration["statsEnabled"], out var connectionStatsEnabledConfig) && connectionStatsEnabledConfig;
var mTlsEnabled = bool.TryParse(builder.Configuration["mTLS"], out var mTlsEnabledConfig) && mTlsEnabledConfig;
var listeningEndpoints = builder.Configuration["urls"] ?? "https://localhost:5000/";

#pragma warning disable CA1416 // Can be launched only on Windows (HttpSys)
builder.WebHost.UseHttpSys(options =>
{
    // meaning client can send a certificate, but it can be explicitly requested by server as well (renegotiation)
    options.ClientCertificateMethod = ClientCertificateMethod.AllowRenegotation;
    options.Authentication.AllowAnonymous = false;
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