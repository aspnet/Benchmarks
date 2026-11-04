using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace NetworkingTransports;

// Where TLS runs. Native: inside a custom transport (the prototype providers), or Kestrel's UseHttps for stock Sockets.
// The other three are Kestrel connection middleware over a plain-TCP transport: SslStream (Kestrel's default),
// TlsSession (the PR dotnet/aspnetcore#69565 data path over the runtime's TlsBufferSession) and a direct Schannel session.
internal enum TlsLayerKind
{
    Native,
    SslStream,
    TlsSession,
    Schannel,
}

internal static class TlsLayer
{
    private static readonly TimeSpan s_handshakeTimeout = TimeSpan.FromSeconds(10);

    public static TlsLayerKind Parse(string? value) => value?.ToLowerInvariant() switch
    {
        null or "" or "native" => TlsLayerKind.Native,
        "sslstream" => TlsLayerKind.SslStream,
        "tlssession" => TlsLayerKind.TlsSession,
        "schannel" => TlsLayerKind.Schannel,
        _ => throw new ArgumentException($"Unknown --tlsLayer '{value}'. Use native, sslstream, tlssession or schannel."),
    };

    public static SslProtocols Protocol => Environment.GetEnvironmentVariable("NETWORKPROTO_WINDOWS_TLS") == "1.2" || !OperatingSystem.IsWindows()
        ? SslProtocols.Tls12
        : SslProtocols.Tls13;

    // Installs the TlsSessionDuplexPipe connection middleware (TlsSession and Schannel layers).
    public static void UseSessionLayer(ListenOptions listen, TlsLayerKind kind, X509Certificate2 certificate)
    {
        var createSession = kind switch
        {
            TlsLayerKind.TlsSession => CreateTlsSessionFactory(certificate),
            TlsLayerKind.Schannel => CreateSchannelFactory(certificate),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        listen.Use(next => async context =>
        {
            var original = context.Transport;
            var tlsPipe = new TlsSessionDuplexPipe(original, createSession());

            try
            {
                using var cancellation = new CancellationTokenSource(s_handshakeTimeout);
                await tlsPipe.HandshakeAsync(cancellation.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or AuthenticationException)
            {
                await tlsPipe.DisposeAsync();
                return;
            }

            try
            {
                context.Transport = tlsPipe;
                await using (tlsPipe)
                {
                    await next(context);
                }
            }
            finally
            {
                context.Transport = original;
            }
        });
    }

    // One TlsContext per endpoint, like the PR; the options mirror the prototype's own TlsSession providers (SessionTls / TlsSessionServer).
    private static Func<ITlsRecordSession> CreateTlsSessionFactory(X509Certificate2 certificate)
    {
        var options = new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            EnabledSslProtocols = Protocol,
            ClientCertificateRequired = false,
            AllowTlsResume = false,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.CipherSuitesPolicy = new CipherSuitesPolicy([TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256]);
        }

        var context = TlsContext.CreateServer(options);
        return () => new TlsBufferRecordSession(context);
    }

    private static Func<ITlsRecordSession> CreateSchannelFactory(X509Certificate2 certificate)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The direct Schannel layer is Windows-only.");
        }

        var credential = new SchannelNative.Credential(certificate);
        return () => new SchannelRecordSession(credential);
    }
}
