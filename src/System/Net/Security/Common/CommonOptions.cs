// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Binding;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace System.Net.Security.Benchmarks.Shared;

public static class ApplicationProtocolConstants
{
    // Reuse known ALPN protocols to leverage special casing optimization in SslStream to avoid allocation
    public readonly static SslApplicationProtocol ReadWrite = SslApplicationProtocol.Http11;
    public readonly static SslApplicationProtocol Handshake = SslApplicationProtocol.Http2;
    public readonly static SslApplicationProtocol Rps = SslApplicationProtocol.Http3;
}

public enum Scenario
{
    // Measure throughput
    ReadWrite,

    // measure number of handshakes per second
    Handshake,

    // measure number of requests per second
    Rps
}

public class CommonOptions
{
    public int Port { get; set; }
    public int ReceiveBufferSize { get; set; }
    public int SendBufferSize { get; set; }
    public X509RevocationMode CertificateRevocationCheckMode { get; set; }

    public override string ToString()
        => $"Port: {Port}, ReceiveBufferSize: {ReceiveBufferSize}, SendBufferSize: {SendBufferSize}, " +
            $"CertificateRevocationCheckMode: {CertificateRevocationCheckMode}";
}

public static class OptionsBinderHelper
{
    public static Option<int> RecieveBufferSizeOption { get; } = new Option<int>("--receive-buffer-size", () => 32 * 1024, "The size of the receive buffer.");
    public static Option<int> SendBufferSizeOption { get; } = new Option<int>("--send-buffer-size", () => 32 * 1024, "The size of the receive buffer, 0 for no writes.");
    public static Option<X509RevocationMode> CertificateRevocationCheckModeOption { get; } = new Option<X509RevocationMode>("--x509-revocation-check-mode", () => X509RevocationMode.NoCheck, "Revocation check mode for the peer certificate.");

    public static void AddOptions(RootCommand command)
    {
        command.AddOption(RecieveBufferSizeOption);
        command.AddOption(SendBufferSizeOption);
        command.AddOption(CertificateRevocationCheckModeOption);
    }

    public static void BindOptions(CommonOptions options, BindingContext bindingContext)
    {
        var parsed = bindingContext.ParseResult;

        options.ReceiveBufferSize = parsed.GetValueForOption(RecieveBufferSizeOption);
        options.SendBufferSize = parsed.GetValueForOption(SendBufferSizeOption);
        options.CertificateRevocationCheckMode = parsed.GetValueForOption(CertificateRevocationCheckModeOption);
    }

    public static X509Certificate2 GenerateSelfSignedCertificate(string hostname, bool isServer)
    {
        var oid = isServer ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2";
        using var rsa = RSA.Create();
        var certReq = new CertificateRequest("CN=" + hostname, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        certReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(oid)], false));
        certReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddMonths(-1), DateTimeOffset.UtcNow.AddMonths(1));
        if (OperatingSystem.IsWindows())
        {
            cert = new X509Certificate2(cert.Export(X509ContentType.Pfx));
        }

        return cert;
    }
}