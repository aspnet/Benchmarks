// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Security.Cryptography.X509Certificates;

namespace System.Net.Benchmarks.Tls;

public class TlsBenchmarkOptions
{
    public int ReceiveBufferSize { get; set; }
    public int SendBufferSize { get; set; }
    public X509RevocationMode CertificateRevocationCheckMode { get; set; }

    public override string ToString()
        => $"ReceiveBufferSize: {ReceiveBufferSize}, SendBufferSize: {SendBufferSize}, " +
            $"CertificateRevocationCheckMode: {CertificateRevocationCheckMode}";
}
