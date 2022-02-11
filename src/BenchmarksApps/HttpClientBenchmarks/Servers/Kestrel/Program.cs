using System.CommandLine;
using System.CommandLine.Invocation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace HttpClientBenchmarks;

class Program
{
    private static ServerOptions s_options = null!;

    private static byte[] s_data10b = new byte[10];
    private static byte[] s_data10k = new byte[10 * 1024];
    private static byte[] s_data10m = new byte[10 * 1024 * 1024];

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand();
        rootCommand.AddOption(new Option<string>(new string[] { "--address" }, "The server address to listen on") { Required = true });
        rootCommand.AddOption(new Option<string>(new string[] { "--port" }, "The server port to listen on") { Required = true });
        rootCommand.AddOption(new Option<bool>(new string[] { "--useHttps" }, () => false, "Whether to use HTTPS"));
        rootCommand.AddOption(new Option<string>(new string[] { "--httpVersion" }, "HTTP Version (1.1 or 2.0 or 3.0)") { Required = true });
        rootCommand.AddOption(new Option<int>(new string[] { "--randomSeed" }, () => 0, "Random seed"));

        rootCommand.Handler = CommandHandler.Create<ServerOptions>(options =>
        {
            s_options = options;
            Log("HttpClient benchmark -- server");
            Log("Options: " + s_options);
            ValidateOptions();
            PrepareData();

            RunKestrel();
        });

        return await rootCommand.InvokeAsync(args);
    }

    private static void ValidateOptions()
    {
        if (!s_options.UseHttps && s_options.HttpVersion == "3.0")
        {
            throw new ArgumentException("HTTP/3.0 only supports HTTPS");
        }
    }

    private static void PrepareData()
    {
        var random = new Random(s_options.RandomSeed);
        random.NextBytes(s_data10b);
        random.NextBytes(s_data10k);
        random.NextBytes(s_data10m);
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

        var url = $"http{(s_options.UseHttps ? "s" : "")}://{s_options.Address}:{s_options.Port}";
        Log("Url: " + url);
        app.Urls.Add(url);

        app.MapGet("/", () => Results.Ok());

        app.MapGet("/get/{responseSize}", GetResponse);

        app.MapPost("/post/{responseSize}", async (HttpRequest request, string responseSize) =>
        {
            await request.Body.CopyToAsync(Stream.Null);
            return GetResponse(responseSize);
        });

        app.Run();
    }

    private static IResult GetResponse(string responseSize)
    {
        return responseSize switch
        {
            "0" => Results.Ok(),
            "10b" => Results.Bytes(s_data10b),
            "10k" => Results.Bytes(s_data10k),
            "10m" => Results.Bytes(s_data10m),
            _ => Results.NotFound()
        };
    }

    private static void Log(string message)
    {
        var time = DateTime.UtcNow.ToString("hh:mm:ss.fff");
        Console.WriteLine($"[{time}] {message}");
    }
}
