// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace System.Net.Benchmarks.Tls;

public class TlsBenchmarkServerOptions : TlsBenchmarkOptions, IBenchmarkServerOptions
{
    public int Port { get; set; }
    public ServerCertSelectionType CertificateSelection { get; set; }
    public bool RequireClientCertificate { get; set; }
    public X509Certificate2 ServerCertificate { get; set; } = null!;
    public List<SslApplicationProtocol> ApplicationProtocols { get; set; } = null!;

    public override string ToString()
    {
        return $"{base.ToString()}, Port: {Port}, CertificateSelection: {CertificateSelection}, RequireClientCertificate: {RequireClientCertificate}, " +
            $"ServerCertificate: {{ {ServerCertificate.ToString().ReplaceLineEndings(" ")}}}";
    }
}
