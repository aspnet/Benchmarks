using HttpSys;
using HttpSys.NetSh;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.HttpSys;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();

// behavioral
var mTlsEnabled = bool.TryParse(builder.Configuration["mTLS"], out var mTlsEnabledConfig) && mTlsEnabledConfig;
var tlsRenegotiationEnabled = bool.TryParse(builder.Configuration["tlsRenegotiation"], out var tlsRenegotiationEnabledConfig) && tlsRenegotiationEnabledConfig;
var listeningEndpoints = builder.Configuration["urls"] ?? "https://localhost:5000/";
var httpsIpPort = listeningEndpoints.Split(";").First(x => x.Contains("https")).Replace("https://", "");

// debug
var writeCertValidationEventsToConsole = bool.TryParse(builder.Configuration["certValidationConsoleEnabled"], out var certValidationConsoleEnabled) && certValidationConsoleEnabled;
var statsEnabled = bool.TryParse(builder.Configuration["statsEnabled"], out var connectionStatsEnabledConfig) && connectionStatsEnabledConfig;
var logRequestDetails = bool.TryParse(builder.Configuration["logRequestDetails"], out var logRequestDetailsConfig) && logRequestDetailsConfig;

var mTLSNetShFlag = mTlsEnabled ? NetShFlag.Enable : NetShFlag.Disabled;

var netshWrapper = new NetShWrapper();

// verify there is an netsh http sslcert binding for specified ip:port
if (!netshWrapper.TryGetSslCertBinding(httpsIpPort, out var sslCertBinding))
{
    Console.WriteLine($"No binding existed. Need to self-sign it and bind to '{httpsIpPort}'");
    if (!netshWrapper.TrySelfSignCertificate(httpsIpPort, out var originalCertThumbprint))
    {
        throw new ApplicationException($"Failed to setup ssl binding for '{httpsIpPort}'. Please unblock the VM.");
    }
    netshWrapper.AddCertBinding(
        httpsIpPort,
        originalCertThumbprint,
        disablesessionid: NetShFlag.Enable,
        enablesessionticket: NetShFlag.Disabled,
        clientCertNegotiation: mTLSNetShFlag);
}

Console.WriteLine("Current netsh ssl certificate binding: \n" + sslCertBinding);

if (
    // those flags can be set only on later versions of HTTP.SYS; so only considering mTLS here
    (netshWrapper.SupportsDisableSessionId && sslCertBinding.DisableSessionIdTlsResumption != NetShFlag.Enable)
    || (netshWrapper.SupportsEnableSessionTicket && (sslCertBinding.EnableSessionTicketTlsResumption == NetShFlag.Enable))
    || sslCertBinding.NegotiateClientCertificate != mTLSNetShFlag)
{
    Console.WriteLine($"Need to prepare ssl-cert binding for the run.");
    Console.WriteLine($"Expected configuration: mTLS={mTLSNetShFlag}; disableSessionId={NetShFlag.Enable}; enableSessionTicket={NetShFlag.Disabled}");

    netshWrapper.UpdateCertBinding(
        httpsIpPort,
        sslCertBinding.CertificateThumbprint,
        appId: sslCertBinding.ApplicationId,
        disablesessionid: NetShFlag.Enable,
        enablesessionticket: NetShFlag.Disabled,
        clientCertNegotiation: mTLSNetShFlag);
}

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

        await next(context);
    });
}

await app.StartAsync();

netshWrapper.LogSslCertBinding(httpsIpPort);

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
Console.WriteLine("Application stopped.");

if (netshWrapper.TryGetSslCertBinding(httpsIpPort, out sslCertBinding) && mTLSNetShFlag == NetShFlag.Enable)
{
    // update the sslCert binding to disable "negotiate client cert" (aka mTLS) to not break other tests.
    Console.WriteLine($"Rolling back mTLS setting for sslCert binding at '{httpsIpPort}'");

    sslCertBinding.NegotiateClientCertificate = NetShFlag.Disabled;
    netshWrapper.UpdateCertBinding(httpsIpPort, sslCertBinding);
}