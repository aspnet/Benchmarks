// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Security;
using System.Net.Sockets;

using System.Net.Benchmarks.Tls;
using System.Net.Benchmarks.Tls.SslStreamBenchmark;

await SslStreamBenchmarkClient.RunAsync(args);

// ----------------------------

internal class SslStreamBenchmarkClient : TlsBenchmarkClient<SslStreamClientConnection, SslClientAuthenticationOptions, SslStreamClientOptions>
{
    public static Task RunAsync(string[] args)
        => new SslStreamBenchmarkClient().RunAsync<SslStreamClientOptionsBinder>(args);

    protected override string Name => "SslStream benchmark client";
    protected override string MetricPrefix => "sslstream";
    protected override void ValidateOptions(SslStreamClientOptions options)
    {
        if (options.Streams != 1)
        {
            throw new ArgumentException("SslStream does not support multiple streams per connection.");
        }
    }

    protected override SslClientAuthenticationOptions CreateClientConnectionOptions(SslStreamClientOptions options)
    {
        var authOptions = CreateSslClientAuthenticationOptions(options);
        authOptions.EnabledSslProtocols = options.EnabledSslProtocols;
#if NET8_0_OR_GREATER
        authOptions.AllowTlsResume = options.AllowTlsResume;
#endif
        return authOptions;
    }

    protected override async Task<SslStreamClientConnection> EstablishConnectionAsync(SslClientAuthenticationOptions authOptions, SslStreamClientOptions options)
    {
        var sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await sock.ConnectAsync(options.Hostname, options.Port).ConfigureAwait(false);

        var networkStream = new NetworkStream(sock, ownsSocket: true);
        var stream = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await stream.AuthenticateAsClientAsync(authOptions).ConfigureAwait(false);

        return new SslStreamClientConnection(stream);
    }
}
