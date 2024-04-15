// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Security;

namespace System.Net.Benchmarks.Tls.SslStreamBenchmark;

internal class SslStreamClientConnection(SslStream _sslStream) : ITlsBenchmarkClientConnection
{
    public Task<Stream> EstablishStreamAsync(TlsBenchmarkClientOptions options)
    {
        Stream stream = _sslStream;
        _sslStream = null!;
        return Task.FromResult(stream);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
