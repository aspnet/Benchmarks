using System.Diagnostics;

namespace SocketBenchmarks.Clients.Basic;

public static class Scenarios
{
    public static bool s_isRunning = true;
    public static bool s_isWarmup = true;
    public static ulong s_errors = 0;

    public static async Task RunConnectionEstablishment(ClientOptions options)
    {
        Utils.Log("Starting Connection Establishment Benchmark");
        Utils.RegisterPercentiledMetric("socket/connectionestablishment", "Connection establishment (ms)", "Connection establishment in milliseconds");

        static async Task<List<double>> Connect(ClientOptions options)
        {
            List<double> results = [];
            while (s_isRunning)
            {
                using Socket socket = new(SocketType.Stream, ProtocolType.Tcp);
                var timestamp = Stopwatch.GetTimestamp();
                await socket.ConnectAsync(options.EndPoint).ConfigureAwait(false);
                if (!s_isWarmup)
                {
                    results.Add(Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);
                }
            }

            return results;
        }

        List<Task<List<double>>> tasks = new(options.Connections);
        for (int i = 0; i < options.Connections; i++)
        {
            tasks.Add(Connect(options));
        }

        await Task.Delay(options.WarmupTime).ConfigureAwait(false);
        s_isWarmup = false;
        Utils.Log("Warmup completed");

        await Task.Delay(options.Duration).ConfigureAwait(false);
        s_isRunning = false;
        Utils.Log("Connection Establishment benchmark completed");

        Utils.Log("Logging results...");

        List<double> results = (await Task.WhenAll(tasks)).SelectMany(x => x).ToList();
        Utils.LogPercentiledMetric("socket/connectionestablishment", results);

        Utils.Log("Finished.");
    }

    public static async Task RunReadWrite(ClientOptions options)
    {
        Utils.Log($"Starting Read Write Tcp Benchmark");
        BenchmarksEventSource.Register("socket/send/tcp/mean", Operations.Avg, Operations.Avg, "Mean bytes sent per second.", "Bytes per second - mean", "n2");
        BenchmarksEventSource.Register("socket/receive/tcp/mean", Operations.Avg, Operations.Avg, "Mean bytes received per second.", "Bytes per second - mean", "n2");

        using Socket socket = new(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(options.EndPoint);

        List<Task<double>> receiveTasks = new(options.Connections);
        List<Task<double>> sendTasks = new(options.Connections);

        for (int i = 0; i < options.Connections; i++)
        {
            receiveTasks.Add(ReceiveTaskTcp(socket, options.ReceiveBufferSize));
            sendTasks.Add(SendTaskTcp(socket, options.SendBufferSize));
        }

        await Task.Delay(options.WarmupTime).ConfigureAwait(false);
        s_isWarmup = false;
        Utils.Log("Warmup completed");

        await Task.Delay(options.Duration).ConfigureAwait(false);
        s_isRunning = false;
        Utils.Log($"Read Write Tcp Benchmark Completed.");
        

        Utils.Log("Logging results...");

        Utils.LogMetric($"socket/receive/tcp/mean", (await Task.WhenAll(receiveTasks)).Sum());
        Utils.LogMetric($"socket/send/tcp/mean", (await Task.WhenAll(sendTasks)).Sum());

        Utils.Log("Finished.");
    }

    public static async Task<double> SendTaskTcp(Socket socket, int sendBufferSize)
    {
        if (sendBufferSize <= 0)
        {
            return 0;
        }

        long bytesSent = 0;
        var sendBuffer = new byte[sendBufferSize];
        Array.Fill(sendBuffer, (byte)0x1);
        Stopwatch stopwatch = new();
        bool isWarmup = true;

        while (s_isRunning)
        {
            if (isWarmup && !s_isWarmup)
            {
                stopwatch.Start();
                isWarmup = false;
            }

            bytesSent += await socket.SendAsync(sendBuffer).ConfigureAwait(false);
        }

        stopwatch.Stop();
        return bytesSent / stopwatch.Elapsed.TotalSeconds;
    }

    public static async Task<double> ReceiveTaskTcp(Socket socket, int receiveBufferSize)
    {
        if (receiveBufferSize <= 0)
        {
            return 0;
        }

        long bytesReceived = 0;
        var receiveBuffer = new byte[receiveBufferSize];
        Stopwatch stopwatch = new();
        bool isWarmup = true;

        while (s_isRunning)
        {
            if (isWarmup && !s_isWarmup)
            {
                stopwatch.Start();
                isWarmup = false;
            }

            bytesReceived += await socket.ReceiveAsync(receiveBuffer).ConfigureAwait(false);
        }

        stopwatch.Stop();
        return bytesReceived / stopwatch.Elapsed.TotalSeconds;
    }
}
