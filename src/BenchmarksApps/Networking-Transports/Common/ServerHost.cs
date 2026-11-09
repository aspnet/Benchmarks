using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NetworkingTransports;

internal sealed record ServerSettings(IPAddress Address, int Port, bool Tls, int Workers, int[]? Cpus, string CertificatePath, string PrivateKeyPath, TlsLayerKind Layer);

internal static class ServerHost
{
    private const int DefaultSize = 1024;
    private const int MaxSize = 256 * 1024;

    private static readonly byte[] s_payload = CreatePayload();

    // useTransport == null keeps the stock Sockets transport. --tls true then terminates TLS in Kestrel (--tlsLayer native/sslstream: UseHttps),
    // or in a connection middleware over the plain stream (--tlsLayer tlssession/schannel).
    // Otherwise the delegate swaps the transport. With --tlsLayer native (the default) that transport owns TLS and Kestrel sees plaintext;
    // with sslstream/tlssession/schannel the transport is plain TCP and the same layers as for Sockets sit on top of it.
    public static async Task RunAsync(string name, string[] args, Action<IWebHostBuilder, ServerSettings>? useTransport = null)
    {
        MemoryDiagnostics.StartIfRequested();

        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();

        var settings = ReadSettings(builder.Configuration);
        useTransport?.Invoke(builder.WebHost, settings);

        var useSslStream = settings.Tls && (settings.Layer == TlsLayerKind.SslStream || (settings.Layer == TlsLayerKind.Native && useTransport is null));
        var useSessionLayer = settings.Tls && settings.Layer is TlsLayerKind.TlsSession or TlsLayerKind.Schannel;
        X509Certificate2? certificate = useSslStream || useSessionLayer ? LoadCertificate(settings) : null;

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(settings.Address, settings.Port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1;
                if (useSslStream)
                {
                    listen.UseHttps(https =>
                    {
                        https.ServerCertificate = certificate;
                        https.SslProtocols = TlsLayer.Protocol;
                        https.OnAuthenticate = (_, ssl) =>
                        {
                            if (!OperatingSystem.IsWindows())
                            {
                                ssl.CipherSuitesPolicy = new CipherSuitesPolicy([TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256]);
                            }
                            ssl.AllowTlsResume = false;
                        };
                    });
                }
                else if (useSessionLayer)
                {
                    TlsLayer.UseSessionLayer(listen, settings.Layer, certificate!);
                }
            });
        });

        var app = builder.Build();

        // GET or POST /{bytes} returns that many bytes (1..262144); any other path returns 1 KiB. A request body is read and discarded.
        app.Run(async context =>
        {
            var request = context.Request;
            if (request.ContentLength is > 0)
            {
                await DrainBodyAsync(request.BodyReader);
            }

            var size = GetSize(request.Path.Value);
            var response = context.Response;
            response.ContentType = "application/octet-stream";
            response.ContentLength = size;
            await response.BodyWriter.WriteAsync(s_payload.AsMemory(0, size));
        });

        Console.WriteLine($"Transport: {name}, listen: {settings.Address}:{settings.Port}, tls: {settings.Tls}, tlsLayer: {settings.Layer}, workers: {settings.Workers}, cpus: {(settings.Cpus is null ? "default" : string.Join(',', settings.Cpus))}");

        MemoryDiagnostics.StopHost = () => app.Lifetime.StopApplication();
        await app.StartAsync();
        Console.WriteLine("Application started.");
        await app.WaitForShutdownAsync();
    }

    private static ServerSettings ReadSettings(IConfiguration configuration)
    {
        var url = (configuration["urls"] ?? "http://0.0.0.0:5000").Split(';')[0].Replace("*", "0.0.0.0").Replace("+", "0.0.0.0");
        var uri = new Uri(url);
        var address = IPAddress.TryParse(uri.Host, out var parsed) ? parsed : IPAddress.Any;
        var certificates = Path.Combine(AppContext.BaseDirectory, "certificates");

        return new ServerSettings(
            address,
            uri.Port,
            configuration.GetValue("tls", false),
            configuration.GetValue("workers", 4),
            configuration["cpus"]?.Split(',').Select(int.Parse).ToArray(),
            configuration["cert"] ?? Path.Combine(certificates, "cert.pem"),
            configuration["key"] ?? Path.Combine(certificates, "key.pem"),
            TlsLayer.Parse(configuration["tlsLayer"]));
    }

    // Same cert.pem/key.pem pair the native transports use, so every transport serves an identical certificate.
    // Re-importing from PFX bytes gives SChannel a persisted key, which a PEM-created key lacks on Windows.
    private static X509Certificate2 LoadCertificate(ServerSettings settings)
    {
        using var pem = X509Certificate2.CreateFromPemFile(settings.CertificatePath, settings.PrivateKeyPath);
        return X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pfx), null);
    }

    private static async Task DrainBodyAsync(PipeReader reader)
    {
        while (true)
        {
            var result = await reader.ReadAsync();
            reader.AdvanceTo(result.Buffer.End);
            if (result.IsCompleted)
            {
                return;
            }
        }
    }

    private static int GetSize(string? path)
    {
        return path is { Length: > 1 } && int.TryParse(path.AsSpan(1), out var size) && size is > 0 and <= MaxSize ? size : DefaultSize;
    }

    private static byte[] CreatePayload()
    {
        var payload = new byte[MaxSize];
        Array.Fill(payload, (byte)'x');
        return payload;
    }
}
