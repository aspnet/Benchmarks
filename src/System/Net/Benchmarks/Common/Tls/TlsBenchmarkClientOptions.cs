// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Security.Cryptography.X509Certificates;

namespace System.Net.Benchmarks.Tls;

public enum Scenario
{
    // Measure throughput
    ReadWrite,

    // measure number of handshakes per second
    Handshake,

    // measure number of requests per second
    Rps
}

public enum CertificateSelectionType
{
    CertContext,
    Collection,
    Callback,
}

public class TlsBenchmarkClientOptions : TlsBenchmarkOptions, IBenchmarkClientOptions
{
    public string Hostname { get; set; } = null!;
    public int Port { get; set; }
    public int Connections { get; set; }
    public int Streams { get; set; }
    public CertificateSelectionType CertificateSelection { get; set; }
    public X509Certificate2? ClientCertificate { get; set; } = null!;
    public string? TlsHostName { get; set; }
    public Scenario Scenario { get; set; }
    public TimeSpan Warmup { get; set; }
    public TimeSpan Duration { get; set; }

    public override string ToString()
        => $"{base.ToString()}, Hostname: {Hostname}, Port: {Port}, Connections: {Connections}, Streams: {Streams}, " +
            $"CertificateSelection: {CertificateSelection}, ClientCertificate: " +
            (ClientCertificate is not null
                ? $"{{ {ClientCertificate.ToString().ReplaceLineEndings(" ")}}}"
                : null) +
            $", TlsHostName: {TlsHostName}, Scenario: {Scenario}, Warmup: {Warmup}, Duration: {Duration}";
}
