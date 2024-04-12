// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

using Common;
using System.Net.Security.Benchmarks.Shared;

namespace System.Net.Security.Benchmarks.Server;

internal interface IListener : IBaseListener<IServerConnection>, IAsyncDisposable
{
    EndPoint LocalEndPoint { get; }
}

internal interface IServerConnection : IAsyncDisposable
{
    SslApplicationProtocol NegotiatedApplicationProtocol { get; }
    Task CompleteHandshakeAsync(CancellationToken cancellationToken);
    Task<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken);
    bool IsMultiplexed { get; }
}

internal abstract class SslBenchmarkServer<TOptions> : BaseServer<IServerConnection, TOptions>
    where TOptions : ServerOptions
{
    public override string GetReadyStateText(IBaseListener<IServerConnection> listener)
        => $"Listening on {((IListener)listener).LocalEndPoint}";

    public override async Task ProcessAsyncInternal(IServerConnection connection, TOptions options, CancellationToken ct)
    {
        try
        {
            await connection.CompleteHandshakeAsync(ct).ConfigureAwait(false);
            var alpn = connection.NegotiatedApplicationProtocol;

            if (alpn == ApplicationProtocolConstants.Handshake)
            {
                return; // all done
            }

            if (alpn == ApplicationProtocolConstants.ReadWrite)
            {
                await AcceptStreamsAsync(connection, options, ReadWriteScenario, ct).ConfigureAwait(false);
                return;
            }

            if (alpn == ApplicationProtocolConstants.Rps)
            {
                await AcceptStreamsAsync(connection, options, RpsScenario, ct).ConfigureAwait(false);
                return;
            }

            throw new Exception($"Negotiated unknown protocol: {connection.NegotiatedApplicationProtocol}");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task AcceptStreamsAsync(IServerConnection connection, ServerOptions options, Func<Stream, ServerOptions, CancellationToken, Task> scenario, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var stream = await connection.AcceptInboundStreamAsync(cancellationToken).ConfigureAwait(false);
            _ = Task.Run(async () =>
                {
                    try
                    {
                        await scenario(stream, options, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        stream.Dispose();
                    }
                }, cancellationToken);

            if (!connection.IsMultiplexed)
            {
                break;
            }
        }
    }

    private static async Task RpsScenario(Stream stream, ServerOptions options, CancellationToken token)
    {
        var sendBuffer = new byte[options.SendBufferSize];
        var recvBuffer = new byte[options.ReceiveBufferSize];

        int totalRead = 0;
        while (true)
        {
            var bytesRead = await stream.ReadAsync(recvBuffer, token).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                if (totalRead > 0)
                {
                    throw new Exception("Unexpected EOF");
                }

                // client closed the connection.
                return;
            }

            totalRead += bytesRead;
            if (totalRead > options.ReceiveBufferSize)
            {
                throw new Exception("Unexpected data received");
            }

            if (totalRead == options.ReceiveBufferSize) // finished reading request
            {
                await stream.WriteAsync(sendBuffer, token).ConfigureAwait(false);
                totalRead = 0;
            }
        }
    }

    private static async Task ReadWriteScenario(Stream stream, ServerOptions options, CancellationToken ct)
    {
        static async Task WritingTask(Stream stream, int bufferSize, CancellationToken linkedCt)
        {
            if (bufferSize == 0)
            {
                return;
            }

            var sendBuffer = new byte[bufferSize];

            try
            {
                while (!linkedCt.IsCancellationRequested)
                {
                    await stream.WriteAsync(sendBuffer, linkedCt).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // expected, just return
            }
        }

        static async Task ReadingTask(Stream stream, int bufferSize, CancellationTokenSource linkedCts)
        {
            var recvBuffer = new byte[Math.Max(bufferSize, 1)];

            while (true)
            {
                var bytesRead = await stream.ReadAsync(recvBuffer, linkedCts.Token).ConfigureAwait(false);

                if (bytesRead > 0 && bufferSize == 0)
                {
                    throw new Exception("Client is sending data but the server is not expecting any");
                }

                if (bytesRead == 0)
                {
                    // client closed the connection.
                    linkedCts.Cancel();
                    break;
                }
            }
        }

        byte[] recvBuffer = new byte[options.ReceiveBufferSize];

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var writeTask = Task.Run(() => WritingTask(stream, options.SendBufferSize, cts.Token), ct);
        var readTask = Task.Run(() => ReadingTask(stream, options.ReceiveBufferSize, cts), ct);

        await Task.WhenAll(writeTask, readTask).ConfigureAwait(false);
    }

    protected static SslServerAuthenticationOptions CreateSslServerAuthenticationOptions(ServerOptions options)
    {
        var sslOptions = new SslServerAuthenticationOptions
        {
            ClientCertificateRequired = options.RequireClientCertificate,
            RemoteCertificateValidationCallback = delegate { return true; },
            ApplicationProtocols = options.ApplicationProtocols,
            CertificateRevocationCheckMode = options.CertificateRevocationCheckMode,
        };

        switch (options.CertificateSelection)
        {
            case ServerCertSelectionType.Certificate:
                sslOptions.ServerCertificate = options.ServerCertificate;
                break;
            case ServerCertSelectionType.Callback:
                sslOptions.ServerCertificateSelectionCallback = delegate { return options.ServerCertificate; };
                break;
            case ServerCertSelectionType.CertContext:
                sslOptions.ServerCertificateContext = SslStreamCertificateContext.Create(options.ServerCertificate, new X509Certificate2Collection());
                break;
        }

        return sslOptions;
    }
}
