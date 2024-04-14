// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Security;
using System.Net.Sockets;

using System.Net.Benchmarks.Tls;
using System.Net.Benchmarks.Tls.SslStream;

await SslStreamBenchmarkClient.RunAsync(args);

// ----------------------------

internal class SslStreamBenchmarkClient : TlsBenchmarkClient<SslStreamClientConnection, SslClientAuthenticationOptions, Options>
{
    public static Task RunAsync(string[] args)
        => new SslStreamBenchmarkClient().RunAsync<OptionsBinder>(args);

    protected override string Name => "SslStream benchmark client";
    protected override string MetricPrefix => "sslstream";
    protected override void ValidateOptions(Options options)
    {
        if (options.Streams != 1)
        {
            throw new ArgumentException("SslStream does not support multiple streams per connection.");
        }
    }

    protected override SslClientAuthenticationOptions CreateClientConnectionOptions(Options options)
    {
        var authOptions = CreateSslClientAuthenticationOptions(options);
        authOptions.EnabledSslProtocols = options.EnabledSslProtocols;
#if NET8_0_OR_GREATER
        authOptions.AllowTlsResume = options.AllowTlsResume;
#endif
        return authOptions;
    }

    protected override async Task<SslStreamClientConnection> EstablishConnectionAsync(SslClientAuthenticationOptions authOptions, Options options)
    {
        var sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await sock.ConnectAsync(options.Hostname, options.Port).ConfigureAwait(false);

        var networkStream = new NetworkStream(sock, ownsSocket: true);
        var stream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await stream.AuthenticateAsClientAsync(authOptions).ConfigureAwait(false);

        return new SslStreamClientConnection(stream);
    }
}

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