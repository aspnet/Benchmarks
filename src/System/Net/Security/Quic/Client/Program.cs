// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.Net;
using System.Net.Quic;

using System.Net.Security.Benchmarks.Client;

internal class Program
{
    private static async Task Main(string[] args)
    {
        await QuicBenchmarkClient.RunCommandAsync(args);
    }
}

// The benchmarks are only run on Windows and Linux
#pragma warning disable CA1416 // "This call site is reachable on all platforms. It is only supported on: 'linux', 'macOS/OSX', 'windows'."

internal class QuicBenchmarkClient : SslBenchmarkClient<QuicClientConnectionOptions, ClientOptions>
{
    public static Task RunCommandAsync(string[] args)
    {
        return new QuicBenchmarkClient().RunCommandAsync<ClientOptionsBinder>(args);
    }
    private QuicBenchmarkClient() { }

    public override string Name => "QUIC benchmark client";
    public override string MetricPrefix => "quic";
    public override void AddCommandLineOptions(RootCommand command) => ClientOptionsBinder.AddOptions(command);
    public override void ValidateOptions(ClientOptions options) { }

    public override QuicClientConnectionOptions CreateClientConnectionOptions(ClientOptions options)
        => new()
        {
            DefaultStreamErrorCode = 123,
            DefaultCloseErrorCode = 456,
            RemoteEndPoint = new DnsEndPoint(options.Hostname, options.Port),
            ClientAuthenticationOptions = CreateSslClientAuthenticationOptions(options)
        };

    public override async Task<IClientConnection> EstablishConnectionAsync(QuicClientConnectionOptions options, ClientOptions _)
        => new QuicClientConnection(await QuicConnection.ConnectAsync(options));
}

internal class QuicClientConnection(QuicConnection _connection) : IClientConnection
{
    private static readonly byte[] s_byteBuf = [ 42 ];

    public async Task<Stream> EstablishStreamAsync(ClientOptions options)
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
