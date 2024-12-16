// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Security.Authentication;

namespace System.Net.Benchmarks.Tls.SslStreamBenchmark;

public class SslStreamClientOptions : TlsBenchmarkClientOptions, ISslStreamExtraOptions
{
    public bool AllowTlsResume { get; set; }
    public SslProtocols EnabledSslProtocols { get; set; }

    public override string ToString()
        => $"{base.ToString()}, AllowTlsResume: {AllowTlsResume}, EnabledSslProtocols: {EnabledSslProtocols}";
}
