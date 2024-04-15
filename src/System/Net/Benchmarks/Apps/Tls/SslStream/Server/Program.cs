// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Net.Sockets;
using System.Net.Benchmarks.Tls;
using System.Net.Benchmarks.Tls.SslStreamBenchmark;

return await SslStreamBenchmarkServer.RunAsync(args).ConfigureAwait(false);

// ----------------------------

internal class SslStreamBenchmarkServer : TlsBenchmarkServer<SslStreamServerListener, SslStreamServerConnection, SslStreamServerOptions>
{
    public static Task<int> RunAsync(string[] args)
        => new SslStreamBenchmarkServer().RunAsync<SslStreamServerOptionsBinder>(args);

    protected override string Name => "SslStream benchmark server";
    protected override string MetricPrefix => "sslstream";

    protected override bool IsExpectedException(Exception e)
        => e is IOException && e.InnerException is SocketException se && se.SocketErrorCode == SocketError.ConnectionReset;

    protected override Task<SslStreamServerListener> ListenAsync(SslStreamServerOptions options, CancellationToken ct)
    {
        var sslOptions = CreateSslServerAuthenticationOptions(options);
        sslOptions.EnabledSslProtocols = options.EnabledSslProtocols;
#if NET8_0_OR_GREATER
        sslOptions.AllowTlsResume = options.AllowTlsResume;
#endif

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.IPv6Any, options.Port));
        socket.Listen();

        var listener = new SslStreamServerListener(socket, sslOptions);
        return Task.FromResult(listener);
    }
}
