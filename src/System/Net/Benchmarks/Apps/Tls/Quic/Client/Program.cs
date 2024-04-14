// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Net.Quic;

using System.Net.Security.Benchmarks;

internal class Program
{
    private static async Task Main(string[] args)
    {
        await QuicBenchmarkClient.RunAsync(args);
    }
}

#pragma warning disable CA1416 // "This call site is reachable on all platforms. It is only supported on: 'linux', 'macOS/OSX', 'windows'."

internal class QuicBenchmarkClient : TlsBenchmarkClient<Connection, QuicClientConnectionOptions, TlsBenchmarkClientOptions>
{
    public static Task RunAsync(string[] args)
        => new QuicBenchmarkClient().RunAsync<TlsBenchmarkClientOptionsBinder<TlsBenchmarkClientOptions>>(args);

    protected override string Name => "QUIC benchmark client";
    protected override string MetricPrefix => "quic";
    protected override void ValidateOptions(TlsBenchmarkClientOptions options) { }

    protected override QuicClientConnectionOptions CreateClientConnectionOptions(TlsBenchmarkClientOptions options)
        => new()
        {
            DefaultStreamErrorCode = 123,
            DefaultCloseErrorCode = 456,
            RemoteEndPoint = new DnsEndPoint(options.Hostname, options.Port),
            ClientAuthenticationOptions = CreateSslClientAuthenticationOptions(options)
        };

    protected override async Task<Connection> EstablishConnectionAsync(QuicClientConnectionOptions options, TlsBenchmarkClientOptions _)
        => new Connection(await QuicConnection.ConnectAsync(options));
}

internal class Connection(QuicConnection _connection) : ITlsBenchmarkClientConnection
{
    private static readonly byte[] s_byteBuf = [ 42 ];

    public async Task<Stream> EstablishStreamAsync(TlsBenchmarkClientOptions options)
    {
        var stream = await _connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
        if (options.SendBufferSize == 0) // we must send at least one byte to open the stream on the wire
        {
            await stream.WriteAsync(s_byteBuf).ConfigureAwait(false);
        }
        return stream;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

#pragma warning restore CA1416
