// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Net.Quic;
using System.Net.Benchmarks.Tls;
using System.Net.Benchmarks.Tls.QuicBenchmark;

return await QuicBenchmarkClient.RunAsync(args).ConfigureAwait(false);

// ----------------------------

internal class QuicBenchmarkClient : TlsBenchmarkClient<QuicClientConnection, QuicClientConnectionOptions, TlsBenchmarkClientOptions>
{
    public static Task<int> RunAsync(string[] args)
        => new QuicBenchmarkClient().RunAsync<TlsBenchmarkClientOptionsBinder<TlsBenchmarkClientOptions>>(args);

    protected override string Name => "QUIC benchmark client";
    protected override string MetricPrefix => "quic";

    protected override QuicClientConnectionOptions CreateClientConnectionOptions(TlsBenchmarkClientOptions options)
        => new()
        {
            DefaultStreamErrorCode = 123,
            DefaultCloseErrorCode = 456,
            RemoteEndPoint = new DnsEndPoint(options.Hostname, options.Port),
            ClientAuthenticationOptions = CreateSslClientAuthenticationOptions(options)
        };

    protected override async Task<QuicClientConnection> EstablishConnectionAsync(QuicClientConnectionOptions options, TlsBenchmarkClientOptions _)
        => new QuicClientConnection(await QuicConnection.ConnectAsync(options));
}
