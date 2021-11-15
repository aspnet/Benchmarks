using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;

// required command line arguments:
// --urls <url starting with http:// or https://>
// (in case https is not used) --Kestrel:EndpointDefaults:Protocols <http1 or http2>

var builder = WebApplication.CreateBuilder(args);
var useHttps = builder.Configuration[WebHostDefaults.ServerUrlsKey]?.StartsWith("https://") ?? false;
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ConfigureEndpointDefaults(listenOptions => 
    {
        if (useHttps)
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
        }
        // in case https is not used, specific protocol (http1/http2) should be passed via command line argument (--Kestrel:EndpointDefaults:Protocols) because of h2c limitations
    });
});
var app = builder.Build();

app.MapGet("/", () => Results.Ok());

app.MapGet("/get", () => "Hello World!");

app.Run();
