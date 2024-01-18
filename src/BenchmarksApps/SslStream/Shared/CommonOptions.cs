using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SslStreamCommon;

public enum Scenario
{
    // Measure throughput
    ReadWrite,

    // measure number of handshakes per second
    Handshake
}

public class OptionsBase
{

    public int Port { get; set; }
    public int ReceiveBufferSize { get; set; }
    public int SendBufferSize { get; set; }
    public bool AllowTlsResume { get; set; }
    public SslProtocols EnabledSslProtocols { get; set; }
    public X509RevocationMode CertificateRevocationCheckMode { get; set; }
}

public static class CommonOptions
{
    public static Option<int> RecieveBufferSizeOption { get; } = new Option<int>("--receive-buffer-size", () => 32 * 1024, "The size of the receive buffer.");
    public static Option<int> SendBufferSizeOption { get; } = new Option<int>("--send-buffer-size", () => 32 * 1024, "The size of the receive buffer, 0 for no writes.");
    public static Option<Version> TlsVersionOption { get; } = new Option<Version>("--tls-version", () => new Version(1, 3), "The TLS protocol version to use.").FromAmong("1.2", "1.3");
    public static Option<bool> AllowTlsResumeOption { get; } = new Option<bool>("--allow-tls-resume", () => true, "Sets TLS session resumption support.");
    public static Option<X509RevocationMode> CertificateRevocationCheckModeOption { get; } = new Option<X509RevocationMode>("--x509-revocation-check-mode", "Revocation check mode for the peer certificate.");

#if !NET8_0_OR_GREATER
    static CommonOptions()
    {
        AllowTlsResumeOption.IsHidden = true;
        AllowTlsResumeOption.AddValidator(symbol =>
        {
            if (!symbol.GetValueOrDefault<bool>())
            {
                return "The option --allow-tls-resume is not supported on this .NET version.";
            }

            return null;
        });
    }
#endif

    public static void AddOptions(RootCommand command)
    {
        command.AddOption(CommonOptions.RecieveBufferSizeOption);
        command.AddOption(CommonOptions.SendBufferSizeOption);
        command.AddOption(CommonOptions.TlsVersionOption);
        command.AddOption(CommonOptions.AllowTlsResumeOption);
        command.AddOption(CommonOptions.CertificateRevocationCheckModeOption);
    }

    public static void BindOptions(OptionsBase options, BindingContext bindingContext)
    {
        var parsed = bindingContext.ParseResult;

        options.ReceiveBufferSize = parsed.GetValueForOption(CommonOptions.RecieveBufferSizeOption);
        options.SendBufferSize = parsed.GetValueForOption(CommonOptions.SendBufferSizeOption);
        options.AllowTlsResume = parsed.GetValueForOption(CommonOptions.AllowTlsResumeOption);
        options.EnabledSslProtocols = parsed.GetValueForOption(CommonOptions.TlsVersionOption) switch
        {
            Version { Major: 1, Minor: 2 } => SslProtocols.Tls12,
            Version { Major: 1, Minor: 3 } => SslProtocols.Tls13,
            _ => throw new InvalidOperationException("Invalid TLS version.")
        };
        options.CertificateRevocationCheckMode = parsed.GetValueForOption(CommonOptions.CertificateRevocationCheckModeOption);
    }

    public static X509Certificate2? GetCertificate(string? path, string? password, string? hostname)
    {
        return string.IsNullOrEmpty(path)
            ? hostname == null ? null : GenerateSelfSignedCertificate(hostname)
            : new X509Certificate2(path, password);
    }

    private static X509Certificate2 GenerateSelfSignedCertificate(string hostname)
    {
        // Create self-signed cert for server.
        using (RSA rsa = RSA.Create())
        {
            var certReq = new CertificateRequest("CN=" + hostname, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            certReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
            certReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
            X509Certificate2 cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
            if (OperatingSystem.IsWindows())
            {
                cert = new X509Certificate2(cert.Export(X509ContentType.Pfx));
            }

            return cert;
        }
    }
}