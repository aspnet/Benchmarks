using Microsoft.AspNetCore.Server.HttpSys;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();

#pragma warning disable CA1416 // Can be launched only on Windows (HttpSys)
builder.WebHost.UseHttpSys(options =>
{
    // meaning client can send a certificate, but it can be explicitly requested by server as well (renegotiation)
    options.ClientCertificateMethod = ClientCertificateMethod.AllowRenegotation;

    options.Authentication.AllowAnonymous = false;
    options.UrlPrefixes.Add("https://*:5000");
});
#pragma warning restore CA1416 // Can be launched only on Windows (HttpSys)

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

var app = builder.Build();

if (mTlsEnabled)
{
    // this is an http.sys middleware to get a cert
    app.Use(async (context, next) => {
        var clientCert = context.Connection.ClientCertificate;
        if (clientCert is null)
        {
            if (writeCertValidationEventsToConsole) Console.WriteLine($"No client certificate provided. Fetching for connection {context.Connection.Id}");
            clientCert = await context.Connection.GetClientCertificateAsync(CancellationToken.None);
        }

        // now we have a client cert and lets imagine we do the validation here
        // if (clientCert.Thumbprint != "1234567890") throw new NotImplementedException();
        await next();
    });
}

app.MapGet("/hello-world", (HttpContext context) =>
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