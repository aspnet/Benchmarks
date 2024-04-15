// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Quic;

namespace System.Net.Benchmarks.Tls.QuicBenchmark;

internal class QuicServerListener(QuicListener _listener, TlsBenchmarkServerOptions _serverOptions) : IListener<QuicServerConnection>
{
    public EndPoint LocalEndPoint => _listener.LocalEndPoint;

    public async Task<QuicServerConnection> AcceptAsync(CancellationToken cancellationToken)
        => new QuicServerConnection(await _listener.AcceptConnectionAsync(cancellationToken), _serverOptions);

    public ValueTask DisposeAsync() => _listener.DisposeAsync();
}
