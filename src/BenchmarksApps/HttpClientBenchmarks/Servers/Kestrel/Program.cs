using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Diagnostics.Tracing;
using System.Text;

var listener = new HttpEventListener();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ConfigureEndpointDefaults(listenOptions => 
    {
        // Create self-signed cert for server.
        using (RSA rsa = RSA.Create())
        {
            var certReq = new CertificateRequest("CN=contoso.com", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            certReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
            certReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
            X509Certificate2 cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
            if (OperatingSystem.IsWindows())
            {
                cert = new X509Certificate2(cert.Export(X509ContentType.Pfx));
            }
            listenOptions.UseHttps(cert);
        }
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
    });
});
var app = builder.Build();

app.MapGet("/", () => Results.Ok());

app.MapGet("/get", () => "Hello World!");

await app.StartAsync();

Console.WriteLine("Application started"); // readyStateText

await app.WaitForShutdownAsync();


class HttpEventListener : EventListener
{
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "Private.InternalDiagnostics.System.Net.Http" || eventSource.Name == "Private.InternalDiagnostics.System.Net.Quic")
            EnableEvents(eventSource, EventLevel.LogAlways);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        var sb = new StringBuilder().Append($"{eventData.TimeStamp:HH:mm:ss.fffffff}[{eventData.EventName}] ");
        for (int i = 0; i < eventData.Payload?.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(eventData.PayloadNames?[i]).Append(": ").Append(eventData.Payload[i]);
        }
        Console.WriteLine(sb.ToString());
    }
}