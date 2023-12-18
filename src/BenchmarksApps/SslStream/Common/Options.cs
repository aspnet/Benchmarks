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

public static class CommonOptions
{
    public static Option<int> PortOption { get; } = new Option<int>("--port", () => 9998, "The server port to listen on") { IsRequired = true };
    public static Option<int> RecieveBufferSizeOption { get; } = new Option<int>("--receive-buffer-size", () => 32 * 1024, "The size of the receive buffer.");
    public static Option<int> SendBufferSizeOption { get; } = new Option<int>("--send-buffer-size", () => 32 * 1024, "The size of the receive buffer, 0 for no writes.");
    public static Option<SslProtocols> SslProtocolsOptions { get; } = new Option<SslProtocols>("--ssl-protocols", "The SSL protocols to use.");

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