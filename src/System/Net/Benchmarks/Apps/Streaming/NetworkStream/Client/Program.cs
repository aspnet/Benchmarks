using System.Diagnostics;
using System.Net;
using System.Net.Benchmarks;
using System.Net.Benchmarks.NetworkStreamBenchmark.Client;
using System.Net.Benchmarks.NetworkStreamBenchmark.Shared;
using System.Net.Sockets;

return await NetworkStreamBenchmarkClient.RunAsync(args).ConfigureAwait(false);

internal class NetworkStreamBenchmarkClient : BenchmarkClient<NetworkStreamClientOptions>
{
    public static Task<int> RunAsync(string[] args)
        => new NetworkStreamBenchmarkClient().RunAsync<NetworkStreamClientOptionsBinder>(args);

    protected override string Name => "Network Stream benchmark client";

    protected override string MetricPrefix => "networkstream";

    protected override async Task RunScenarioAsync(NetworkStreamClientOptions options, CancellationToken cancellationToken)
    {
        switch (options.Scenario)
        {
            case Scenario.ConnectionEstablishment:
                await RunConnectionEstablishmentScenario(options, cancellationToken).ConfigureAwait(false);
                break;
            case Scenario.ReadWrite:
                await RunReadWriteScenario(options, cancellationToken).ConfigureAwait(false);
                break;
            case Scenario.Rps:
                await RunRpsScenario(options, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException("Invalid scenario.");
        }
    }

    private async Task<NetworkStreamClientConnection> EstablishConnectionAsync(NetworkStreamClientOptions options)
    {
        Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(options.Address!, options.Port));

        return new NetworkStreamClientConnection(new NetworkStream(socket, ownsSocket: true));
    }

    internal record ReadWriteMetrics(double BytesReadPerSecond, double BytesWrittenPerSecond);
    private async Task RunConnectionEstablishmentScenario(NetworkStreamClientOptions options, CancellationToken cancellationToken)
    {
        var tasks = new List<Task<List<double>>>(options.Connections);
        for (var i = 0; i < options.Connections; i++)
        {
            tasks.Add(ConnectionEstablishmentScenario(EstablishConnectionAsync, options));
        }

        var metrics = await Task.WhenAll(tasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        LogMetricPercentiles(MetricName.ConnectionEstablishment, "Connection establishment duration (ms)", metrics.SelectMany(x => x).ToList());
    }

    private async Task RunReadWriteScenario(NetworkStreamClientOptions options, CancellationToken cancellationToken)
    {
        var connections = new NetworkStreamClientConnection[options.Connections];
        var tasks = new List<Task<ReadWriteMetrics>>(options.Connections);
        for (var i = 0; i < options.Connections; i++)
        {
            connections[i] = await EstablishConnectionAsync(options).ConfigureAwait(false);
            tasks.Add(ReadWriteScenario(connections[i], options));
        }

        var metrics = await Task.WhenAll(tasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        LogMetric(MetricName.Read + MetricName.Mean, "Read B/s - mean", metrics.Sum(x => x.BytesReadPerSecond));
        LogMetric(MetricName.Write + MetricName.Mean, "Write B/s - mean", metrics.Sum(x => x.BytesWrittenPerSecond));

        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private async Task RunRpsScenario(NetworkStreamClientOptions options, CancellationToken cancellationToken)
    {
        var connections = new NetworkStreamClientConnection[options.Connections];
        var tasks = new List<Task<(double Rps, long Errors)>>(options.Connections);
        for (var i = 0; i < options.Connections; i++)
        {
            connections[i] = await EstablishConnectionAsync(options).ConfigureAwait(false);
            tasks.Add(RpsScenario(connections[i], options));
        }

        var metrics = await Task.WhenAll(tasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        LogMetric(MetricName.Rps + MetricName.Mean, "RPS - mean", metrics.Sum(x => x.Rps));
        if (metrics.Any(x => x.Errors > 0))
        {
            LogMetric(MetricName.Errors, "Errors", metrics.Sum(x => x.Errors), "n0");
        }

        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private static async Task<List<double>> ConnectionEstablishmentScenario(Func<NetworkStreamClientOptions, Task<NetworkStreamClientConnection>> establishConnectionAsync, NetworkStreamClientOptions options)
    {
        var values = new List<double>((int)options.Duration.TotalMilliseconds);
        var isWarmup = true;
        var sw = Stopwatch.StartNew();
        while (IsRunning)
        {
            if (isWarmup && !IsWarmup)
            {
                isWarmup = false;
                values.Clear();
                OnWarmupCompleted();
            }
            sw.Restart();
            await using var connection = await establishConnectionAsync(options).ConfigureAwait(false);
            await using var stream = await connection.EstablishStreamAsync(options).ConfigureAwait(false);
            var elapsedMs = sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
            values.Add(elapsedMs);
        }
        return values;
    }

    private static async Task<ReadWriteMetrics> ReadWriteScenario(NetworkStreamClientConnection connection, NetworkStreamClientOptions options)
    {
        await using var s = await connection.EstablishStreamAsync(options).ConfigureAwait(false);

        // spawn the reading and writing tasks on the thread pool, note that if we were to use
        //     var writeTask = WritingTask(stream, options.SendBufferSize);
        // then it could end up writing a lot of data until it finally suspended control back to this function.
        var writeTask = Task.Run(() => WritingTask(s, options.SendBufferSize));
        var readTask = Task.Run(() => ReadingTask(s, options.ReceiveBufferSize));

        await Task.WhenAll(writeTask, readTask).ConfigureAwait(false);
        return new ReadWriteMetrics(BytesReadPerSecond: await readTask, BytesWrittenPerSecond: await writeTask);

        async Task<double> WritingTask(Stream stream, int bufferSize)
        {
            if (bufferSize == 0)
            {
                return 0;
            }

            var sendBuffer = new byte[bufferSize];
            var sw = Stopwatch.StartNew();
            var isWarmup = true;

            long bytesWritten = 0;

            while (IsRunning)
            {
                if (isWarmup && !IsWarmup)
                {
                    isWarmup = false;
                    bytesWritten = 0;
                    OnWarmupCompleted();
                    sw.Restart();
                }

                await stream.WriteAsync(sendBuffer, default).ConfigureAwait(false);
                bytesWritten += bufferSize;
            }

            sw.Stop();
            var elapsed = sw.ElapsedTicks * 1.0 / Stopwatch.Frequency;
            return bytesWritten / elapsed;
        }

        async Task<double> ReadingTask(Stream stream, int bufferSize)
        {
            if (bufferSize == 0)
            {
                return 0;
            }

            var recvBuffer = new byte[bufferSize];
            var sw = Stopwatch.StartNew();
            var isWarmup = true;

            long bytesRead = 0;

            while (IsRunning)
            {
                if (isWarmup && !IsWarmup)
                {
                    if (bytesRead == 0)
                    {
                        throw new Exception($"No bytes read during warmup.");
                    }
                    isWarmup = false;
                    bytesRead = 0;
                    OnWarmupCompleted();
                    sw.Restart();
                }

                bytesRead += await stream.ReadAsync(recvBuffer, default).ConfigureAwait(false);
            }

            sw.Stop();
            var elapsed = sw.ElapsedTicks * 1.0 / Stopwatch.Frequency;
            return bytesRead / elapsed;
        }
    }

    private static async Task<(double Rps, long Errors)> RpsScenario(NetworkStreamClientConnection connection, NetworkStreamClientOptions options)
    {
        await using var stream = await connection.EstablishStreamAsync(options).ConfigureAwait(false);

        var sendBuffer = new byte[options.SendBufferSize];
        var receiveBuffer = new byte[options.ReceiveBufferSize];

        long successRequests = 0;
        long exceptionRequests = 0;

        var isWarmup = true;
        var sw = Stopwatch.StartNew();

        while (IsRunning)
        {
            if (isWarmup && !IsWarmup)
            {
                if (successRequests == 0)
                {
                    throw new Exception($"No successful requests during warmup.");
                }
                isWarmup = false;
                successRequests = 0;
                exceptionRequests = 0;
                OnWarmupCompleted();
                sw.Restart();
            }

            try
            {
                await stream.WriteAsync(sendBuffer, default).ConfigureAwait(false);
                await stream.ReadExactlyAsync(receiveBuffer, 0, receiveBuffer.Length, default).ConfigureAwait(false);
                successRequests++;
            }
            catch (Exception ex)
            {
                Log("Exception: " + ex);
                exceptionRequests++;
            }
        }
        var elapsed = sw.ElapsedTicks * 1.0 / Stopwatch.Frequency;
        return (successRequests / elapsed, exceptionRequests);
    }
}