// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Security;

namespace System.Net.Benchmarks.Tls.SslStreamBenchmark;

internal class SslStreamClientConnection : SingleStreamConnection<SslStream>, ITlsBenchmarkClientConnection
{
    public SslStreamClientConnection(SslStream _sslStream) => InnerStream = _sslStream;

    public Task<Stream> EstablishStreamAsync(TlsBenchmarkClientOptions options) => Task.FromResult<Stream>(ConsumeStream());
}
