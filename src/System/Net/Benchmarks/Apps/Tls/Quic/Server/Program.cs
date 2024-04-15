// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Net.Quic;
using System.Net.Benchmarks.Tls;
using System.Net.Benchmarks.Tls.QuicBenchmark;

return await QuicBenchmarkServer.RunAsync(args).ConfigureAwait(false);

// ----------------------------

internal class QuicBenchmarkServer : TlsBenchmarkServer<QuicServerListener, QuicServerConnection, TlsBenchmarkServerOptions>
{
    public static Task<int> RunAsync(string[] args)
        => new QuicBenchmarkServer().RunAsync<TlsBenchmarkServerOptionsBinder<TlsBenchmarkServerOptions>>(args);

    protected override string Name => "QUIC benchmark server";
    protected override string MetricPrefix => "quic";

    protected override bool IsExpectedException(Exception e)
        => e is QuicException qe && qe.QuicError == QuicError.ConnectionAborted;

    protected override async Task<QuicServerListener> ListenAsync(TlsBenchmarkServerOptions options, CancellationToken ct)
    {
        var sslOptions = CreateSslServerAuthenticationOptions(options);
        var connectionOptions = new QuicServerConnectionOptions()
        {
            DefaultStreamErrorCode = 123,
            DefaultCloseErrorCode = 456,
            ServerAuthenticationOptions = sslOptions,
            MaxInboundBidirectionalStreams = 20000
        };
        var listenerOptions = new QuicListenerOptions()
        {
            ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, options.Port),
            ApplicationProtocols = connectionOptions.ServerAuthenticationOptions.ApplicationProtocols!,
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(connectionOptions)
        };

        return new QuicServerListener(await QuicListener.ListenAsync(listenerOptions, ct), options);
    }
}
