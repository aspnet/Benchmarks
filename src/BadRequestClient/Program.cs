using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Crank.EventSources;

namespace BadRequestClient;

class Program
{
    private static string Ip = "127.0.0.1";
    private static int Port = 5000;
    private static int Connections = 1;
    private static TimeSpan Warmup = TimeSpan.Zero;
    private static TimeSpan Duration = TimeSpan.FromSeconds(15);
    private static string RequestType = "invalid-header";
    private static Stopwatch _stopwatch = Stopwatch.StartNew();

    // Number of active connections
    private static int _connections;

    // Number of completed requests
    private static long _requests;

    // Number of errors
    private static long _errors;

    // When the threads need to be stopped
    private static volatile bool _stopped;

    // When the threads need to start measuring
    private static volatile bool _measure;

    static async Task<int> Main(string[] args)
    {
        var app = new CommandLineApplication();
        app.HelpOption("-h|--help");

        var optionIp = app.Option("-a|--address <IP>", "The server IP address", CommandOptionType.SingleValue);
        var optionPort = app.Option("-p|--port <PORT>", "The server port. Default is 5000", CommandOptionType.SingleValue);
        var optionConnections = app.Option<int>("-c|--connections <N>", "Number of concurrent connections", CommandOptionType.SingleValue);
        var optionWarmup = app.Option<int>("-w|--warmup <N>", "Duration of warmup in seconds", CommandOptionType.SingleValue);
        var optionDuration = app.Option<int>("-d|--duration <N>", "Duration of test in seconds", CommandOptionType.SingleValue);
        var optionType = app.Option("-t|--type <TYPE>", "Request type: valid, invalid-header, no-colon, invalid-method, invalid-version, null-byte, tls-over-http, long-header, control-chars", CommandOptionType.SingleValue);

        // Register metrics with crank
        BenchmarksEventSource.Register("badrequest/connections", Operations.Max, Operations.Sum, "Connections", "Number of active connections", "n0");
        BenchmarksEventSource.Register("badrequest/requests", Operations.Max, Operations.Sum, "Requests", "Total completed requests", "n0");
        BenchmarksEventSource.Register("badrequest/errors", Operations.Max, Operations.Sum, "Errors", "Total errors", "n0");
        BenchmarksEventSource.Register("badrequest/rps/mean", Operations.Max, Operations.Sum, "Requests/sec", "Average requests per second", "n0");
        BenchmarksEventSource.Register("badrequest/rps/max", Operations.Max, Operations.Max, "Max RPS", "Max requests per second", "n0");

        app.OnExecuteAsync(async cancellationToken =>
        {
            Console.WriteLine("Bad Request Benchmark Client");

            if (optionIp.HasValue())
            {
                Ip = optionIp.Value()!;
            }

            if (optionPort.HasValue())
            {
                Port = int.Parse(optionPort.Value()!);
            }

            if (optionConnections.HasValue())
            {
                Connections = int.Parse(optionConnections.Value()!);
            }

            if (optionWarmup.HasValue())
            {
                Warmup = TimeSpan.FromSeconds(int.Parse(optionWarmup.Value()!));
            }

            if (optionDuration.HasValue())
            {
                Duration = TimeSpan.FromSeconds(int.Parse(optionDuration.Value()!));
            }

            if (optionType.HasValue())
            {
                RequestType = optionType.Value()!;
            }

            var requestBytes = GetRequestBytes(RequestType);
            var isValidRequest = RequestType == "valid";

            Console.WriteLine($"Target: {Ip}:{Port}");
            Console.WriteLine($"Warmup: {Warmup.TotalSeconds}s");
            Console.WriteLine($"Duration: {Duration.TotalSeconds}s");
            Console.WriteLine($"Connections: {Connections}");
            Console.WriteLine($"Request type: {RequestType}");
            Console.WriteLine($"Request ({requestBytes.Length} bytes):");
            Console.WriteLine($"  {Encoding.ASCII.GetString(requestBytes).Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0")}");
            Console.WriteLine();

            BenchmarksEventSource.Measure("badrequest/connections", Connections);

            // Start results writer
            _ = WriteResultsAsync();

            // Start scheduler
            _ = ScheduleAsync();

            // Run client
            await RunClientAsync(requestBytes, isValidRequest);

            return 0;
        });

        return await app.ExecuteAsync(args);
    }

    private static async Task ScheduleAsync()
    {
        await Task.Delay(Warmup);

        Interlocked.Exchange(ref _requests, 0);
        Interlocked.Exchange(ref _errors, 0);

        _measure = true;
        _stopwatch.Restart();

        await Task.Delay(Duration);

        _stopped = true;
    }

    private static async Task RunClientAsync(byte[] requestBytes, bool keepAlive)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(Ip), Port);
        var tasks = new Task[Connections];

        for (var i = 0; i < Connections; i++)
        {
            tasks[i] = Task.Run(() => RunConnectionAsync(endpoint, requestBytes, keepAlive));
        }

        await Task.WhenAll(tasks);
    }

    private static async Task RunConnectionAsync(IPEndPoint endpoint, byte[] request, bool keepAlive)
    {
        var responseBuffer = new byte[4096];
        Socket? socket = null;
        Interlocked.Increment(ref _connections);

        try
        {
            while (!_stopped)
            {
                try
                {
                    // Create new connection if needed
                    if (socket == null)
                    {
                        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        socket.NoDelay = true;
                        await socket.ConnectAsync(endpoint);
                    }

                    // Send request
                    await socket.SendAsync(request, SocketFlags.None);

                    // Read response
                    var received = await socket.ReceiveAsync(responseBuffer, SocketFlags.None);

                    if (received > 0)
                    {
                        Interlocked.Increment(ref _requests);

                        // For bad requests, server closes connection - need new socket
                        if (!keepAlive)
                        {
                            socket.Dispose();
                            socket = null;
                        }
                    }
                    else
                    {
                        // Connection closed - counts as completed request
                        Interlocked.Increment(ref _requests);
                        socket.Dispose();
                        socket = null;
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset ||
                                                  ex.SocketErrorCode == SocketError.ConnectionRefused ||
                                                  ex.SocketErrorCode == SocketError.ConnectionAborted)
                {
                    // Connection closed by server
                    Interlocked.Increment(ref _requests);
                    socket?.Dispose();
                    socket = null;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse ||
                                                  ex.SocketErrorCode == SocketError.TooManyOpenSockets)
                {
                    // Socket exhaustion - back off
                    socket?.Dispose();
                    socket = null;
                    Interlocked.Increment(ref _errors);
                    await Task.Delay(1);
                }
                catch (SocketException)
                {
                    socket?.Dispose();
                    socket = null;
                    Interlocked.Increment(ref _errors);
                }
                catch (ObjectDisposedException)
                {
                    socket = null;
                }
            }
        }
        finally
        {
            socket?.Dispose();
            Interlocked.Decrement(ref _connections);
        }
    }

    private static async Task WriteResultsAsync()
    {
        var lastRequests = 0L;
        var lastElapsed = TimeSpan.Zero;

        while (!_stopped)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));

            if (!_measure)
            {
                continue;
            }

            var requests = _requests;
            var errors = _errors;
            var currentRequests = requests - lastRequests;
            lastRequests = requests;

            var elapsed = _stopwatch.Elapsed;
            var currentElapsed = elapsed - lastElapsed;
            lastElapsed = elapsed;

            var currentRps = currentRequests / currentElapsed.TotalSeconds;
            var averageRps = requests / elapsed.TotalSeconds;

            Console.WriteLine(
                $"{DateTime.UtcNow:o}\tRequests\t{requests}" +
                $"\tErrors\t{errors}" +
                $"\tCurrent RPS\t{currentRps:N0}" +
                $"\tAverage RPS\t{averageRps:N0}" +
                $"\tConnections\t{_connections}");

            BenchmarksEventSource.Measure("badrequest/requests", requests);
            BenchmarksEventSource.Measure("badrequest/errors", errors);
            BenchmarksEventSource.Measure("badrequest/rps/max", currentRps);
            BenchmarksEventSource.Measure("badrequest/rps/mean", averageRps);
        }
    }

    private static byte[] GetRequestBytes(string type)
    {
        return type switch
        {
            // Valid request for baseline comparison
            "valid" => Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: localhost\r\n\r\n"),

            // Invalid header name (contains space)
            "invalid-header" => Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nBad Header: value\r\n\r\n"),

            // Invalid header (no colon)
            "no-colon" => Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nBadHeaderNoColon\r\n\r\n"),

            // Invalid request line (bad method)
            "invalid-method" => Encoding.ASCII.GetBytes("G\0ET / HTTP/1.1\r\n\r\n"),

            // Invalid HTTP version
            "invalid-version" => Encoding.ASCII.GetBytes("GET / HTTP/9.9\r\n\r\n"),

            // Null byte in request line
            "null-byte" => Encoding.ASCII.GetBytes("GET /\0path HTTP/1.1\r\n\r\n"),

            // TLS over HTTP (simulated - first byte is 0x16)
            "tls-over-http" => [0x16, 0x03, 0x01, 0x02, 0x00],

            // Very long header name
            "long-header" => Encoding.ASCII.GetBytes($"GET / HTTP/1.1\r\n{new string('X', 1000)}: value\r\n\r\n"),

            // Invalid characters in header value (control chars)
            "control-chars" => Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: local\x01host\r\n\r\n"),

            _ => throw new ArgumentException($"Unknown request type: {type}")
        };
    }
}
