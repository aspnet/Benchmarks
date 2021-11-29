using System.CommandLine;
using System.CommandLine.Invocation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace HttpClientBenchmarks;

class Program
{
    private static ServerOptions s_options = null!;
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand();
        rootCommand.AddOption(new Option<string>(new string[] { "--url" }, "The server url to listen on") { Required = true });
        rootCommand.AddOption(new Option<string>(new string[] { "--httpVersion" }, "HTTP Version (1.1 or 2.0 or 3.0)") { Required = true });

        rootCommand.Handler = CommandHandler.Create<ServerOptions>(options =>
        {
            s_options = options;
            Log("HttpClient benchmark -- server");
            Log("Options: " + s_options);
            ValidateOptions();

            RunKestrel();
        });

        return await rootCommand.InvokeAsync(args);
    }

    private static void ValidateOptions()
    {
        bool isHttp = s_options.Url!.StartsWith("http://");
        bool isHttps = s_options.Url!.StartsWith("https://");

        if (!isHttp && !isHttps)
        {
            throw new ArgumentException("Unsupported URL format: " + s_options.Url);
        }

        if (!isHttps && s_options.HttpVersion == "3.0")
        {
            throw new ArgumentException("HTTP/3.0 only supports HTTPS");
        }
    }

    public static void RunKestrel()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.ConfigureHttpsDefaults(httpsOptions =>
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
                    httpsOptions.ServerCertificate = cert;
                }
            });
            serverOptions.ConfigureEndpointDefaults(listenOptions => 
            {
                listenOptions.Protocols = s_options.HttpVersion switch
                {
                    "1.1" => HttpProtocols.Http1,
                    "2.0" => HttpProtocols.Http2,
                    "3.0" => HttpProtocols.Http3,
                    _ => throw new ArgumentException("Unsupported HTTP version: " + s_options.HttpVersion)
                };
            });
        });
        var app = builder.Build();
        app.Urls.Add(s_options.Url!);

        app.MapGet("/", () => Results.Ok());

        app.MapGet("/get", () => "Hello World!");

        app.Run();
    }

    private static void Log(string message)
    {
        var time = DateTime.UtcNow.ToString("hh:mm:ss.fff");
        Console.WriteLine($"[{time}] {message}");
    }
}
