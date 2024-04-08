namespace SocketBenchmarks.Servers.Basic;

class Program
{
    private static int s_errors = 0;
    public static async Task<int> Main(string[] args)
    {
        RootCommand rootCmd = new("Socket server");
        ServerOptionsBinder.AddOptions(rootCmd);
        rootCmd.SetHandler(RunServer, new ServerOptionsBinder());
        return await rootCmd.InvokeAsync(args).ConfigureAwait(false);
    }

    public static async Task RunServer(ServerOptions options)
    {
        RegisterBenchmarks();
        CancellationTokenSource cts = new();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        if (options.MaxThreadCount > 0)
        {
            Log($"Max Thread Count : {options.MaxThreadCount}");
            ThreadPool.GetMaxThreads(out int workerThreadsCount, out int _);
            if (!ThreadPool.SetMaxThreads(workerThreadsCount, options.MaxThreadCount))
            {
                throw new Exception("Failed to set max thread count");
            }
        }

        Log($"Starting TCP server");

        Socket socket = new(SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.IPv6Any, options.Port));
        socket.Listen();
        Log($"Listening on : {socket.LocalEndPoint!}");

        while (!cts.IsCancellationRequested)
        {
            Socket acceptedSocket = await socket.AcceptAsync().ConfigureAwait(false);
            _ = HandleClient(acceptedSocket, options);
        }

        LogMetric("socket/errors", s_errors);
        Log("Server stopped");
    }

    public static async Task HandleClient(Socket socket, ServerOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (options.Scenario)
            {
                case Scenario.ConnectionEstablishment:
                    // Nothing to do, just return.
                    return;
                case Scenario.ReadWrite:
                    await Task.WhenAll(
                        ReadTask(socket, options.ReceiveBufferSize, cancellationToken),
                        WriteTask(socket, options.SendBufferSize, cancellationToken)
                    );
                    break;
                default:
                    throw new NotSupportedException("Scenario not supported");
            }
        }
        catch (Exception ex)
        {
            Log($"Exception: {ex}");
            Interlocked.Increment(ref s_errors);
        }

        static async Task ReadTask(Socket socket, int receiveBufferSize, CancellationToken cancellationToken)
        {
            if (receiveBufferSize <= 0)
            {
                return;
            }

            byte[] receiveBuffer = new byte[receiveBufferSize];
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await socket.ReceiveAsync(receiveBuffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }
            }
        }

        static async Task WriteTask(Socket socket, int sendBufferSize, CancellationToken cancellationToken)
        {
            if (sendBufferSize <= 0)
            {
                return;
            }

            byte[] sendBuffer = new byte[sendBufferSize];
            Array.Fill(sendBuffer, (byte)0x1);
            while (!cancellationToken.IsCancellationRequested)
            {
                await socket.SendAsync(sendBuffer, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static void RegisterBenchmarks()
    {
        BenchmarksEventSource.Register("socket/errors", Operations.First, Operations.First, "Connection Error Count", "Socket Connection Error Count", "n0");
        LogMetric("env/processorcount", Environment.ProcessorCount);
    }

    public static void Log(string message)
    {
        var time = DateTime.UtcNow.ToString("hh:mm:ss.fff");
        Console.WriteLine($"[{time}] {message}");
    }

    public static void LogMetric(string name, long value)
    {
        BenchmarksEventSource.Measure(name, value);
        Log($"{name}: {value}");
    }

    public static void LogMetric(string name, double value)
    {
        BenchmarksEventSource.Measure(name, value);
        Log($"{name}: {value}");
    }
}