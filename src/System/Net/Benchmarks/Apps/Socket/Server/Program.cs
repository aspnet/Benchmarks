// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Net.Benchmarks;
using System.Net.Benchmarks.SocketBenchmark;
using System.Net.Benchmarks.SocketBenchmark.Shared;
using System.Net.Sockets;

return await SocketBenchmarkServer.RunAsync(args).ConfigureAwait(false);

internal class SocketBenchmarkServer : BenchmarkServer<SocketServerListener, SocketServerConnection, SocketServerOptions>
{
    public static Task<int> RunAsync(string[] args)
        => new SocketBenchmarkServer().RunAsync<SocketServerOptionsBinder>(args);
    protected override string Name => "Socket server";

    protected override string MetricPrefix => "socket";

    protected override Task<SocketServerListener> ListenAsync(SocketServerOptions options, CancellationToken ct)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.IPv6Any, options.Port));
        socket.Listen();

        return Task.FromResult(new SocketServerListener(socket));
    }

    protected override async Task ProcessAcceptedAsync(SocketServerConnection accepted, SocketServerOptions options, CancellationToken ct)
    {
        Scenario scenario = options.Scenario;
        if (scenario == Scenario.ConnectionEstablishment)
        {
            // Do nothing.
            return;
        }

        Stream stream = await accepted.EstablishStreamAsync();

        if (scenario == Scenario.ReadWrite)
        {
            await ReadWriteScenario(stream, options, ct).ConfigureAwait(false);
            return;
        }

        if (scenario == Scenario.Rps)
        {
            await RpsScenario(stream, options, ct).ConfigureAwait(false);
            return;
        }
    }

    private static async Task RpsScenario(Stream stream, SocketServerOptions options, CancellationToken token)
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

                // client closed the connection
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

    private static async Task ReadWriteScenario(Stream stream, SocketServerOptions options, CancellationToken ct)
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
                    // client closed the connection
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
}