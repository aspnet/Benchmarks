using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks;
using McMaster.Extensions.CommandLineUtils;

namespace TcpClient
{
    class Program
    {
        private static string Ip;
        private static int Port = 5201;
        private static int Size = 1;
        private static int WarmupSeconds;
        private static int DurationSeconds;
        private static int Connections;
        private static Stopwatch _stopwatch = Stopwatch.StartNew();

        // Number of active connections
        private static int _connections;

        // Number of requests sent
        private static long _requests;

        // When the threads need to be stopped
        private static bool _stopped;

        // When the threads need to start measuring
        private static bool _measure;

        static async Task Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var optionIp = app.Option("-a|--address <IP>", "The server IP address", CommandOptionType.SingleValue);
            var optionPort = app.Option("-p|--port <PORT>", "The server port. Default is 5201", CommandOptionType.SingleValue);
            var optionSize = app.Option("-s|--size <N>", "The size of the payload in bytes", CommandOptionType.SingleValue);
            var optionConnections = app.Option<int>("-c|--connections <N>", "Total number of TCP connections to use", CommandOptionType.SingleValue);
            var optionWarmup = app.Option<int>("-w|--warmup <N>", "Duration of the warmup in seconds", CommandOptionType.SingleValue);
            var optionDuration = app.Option<int>("-d|--duration <N>", "Duration of the test in seconds", CommandOptionType.SingleValue);

            BenchmarksEventSource.Log.Metadata("tcpclient/connections", "max", "sum", "Connections", "Number of active connections", "n0");
            BenchmarksEventSource.Log.Metadata("tcpclient/rps", "max", "sum", "RPS", "Average requests per second", "n0");
            BenchmarksEventSource.Log.Metadata("tcpclient/max-rps", "max", "sum", "Max RPS", "Max requests per second", "n0");
            BenchmarksEventSource.Log.Metadata("tcpclient/requests", "max", "sum", "Requests", "Total number of requests per second", "n0");

            BenchmarksEventSource.Measure("tcpclient/connections", Connections);

            app.OnExecuteAsync(cancellationToken =>
            {
                Console.WriteLine("TCP Client");

                Ip = optionIp.Value();

                if (optionPort.HasValue())
                {
                    Port = int.Parse(optionPort.Value());
                }

                if (optionSize.HasValue())
                {
                    Size = int.Parse(optionSize.Value());
                }

                WarmupSeconds = optionWarmup.HasValue()
                    ? int.Parse(optionWarmup.Value())
                    : 0;

                DurationSeconds = int.Parse(optionDuration.Value());

                Connections = int.Parse(optionConnections.Value());

                // Schedules a task that outputs the results continuously
                WriteResults();

                // Schedules a task that starts and stops the warmup and the measurement
                ScheduleAsync();

                // Blocking until _stopped is set to true
                RunClient();

                return Task.CompletedTask;
            });

            await app.ExecuteAsync(args);            
        }

        private static async Task ScheduleAsync()
        {
            await Task.Delay(WarmupSeconds);

            Interlocked.Exchange(ref _requests, 0);

            _measure = true;

            await Task.Delay(DurationSeconds);

            _stopped = true;
        }

        private static void RunClient()
        {
            var payload = new byte[Size];

            for (var x = 0; x < Size; x++)
            {
                payload[x] = 1;
            }

            var threads = new Thread[Connections];

            for (var i = 0; i < Connections; i++)
            {
                var thread = new Thread(() =>
                {
                    using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                    {
                        socket.Connect(Ip, Port);
                        Interlocked.Increment(ref _connections);

                        try
                        {
                            var buffer = new byte[Size];
                            while (!_stopped)
                            {
                                socket.Send(payload);
                                socket.Receive(buffer);
                                Interlocked.Increment(ref _requests);
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                            Environment.Exit(1);
                        }
                    }
                });
                threads[i] = thread;
                thread.Start();
            }

            foreach (var thread in threads)
            {
                thread.Join();
            }
        }

        private static async Task WriteResults()
        {
            var lastRequests = (long)0;
            var lastElapsed = TimeSpan.Zero;

            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));

                if (!_measure)
                {
                    continue;
                }

                var requests = _requests;
                var currentPackets = requests - lastRequests;
                lastRequests = requests;

                var elapsed = _stopwatch.Elapsed;
                var currentElapsed = elapsed - lastElapsed;
                lastElapsed = elapsed;

                WriteResult(requests, elapsed, currentPackets, currentElapsed);
            }
        }

        private static void WriteResult(long totalRequests, TimeSpan totalElapsed, long currentPackets, TimeSpan currentElapsed)
        {
            Console.WriteLine(
                $"{DateTime.UtcNow.ToString("o")}\tTotal Requests\t{totalRequests}" +
                $"\tCurrent RPS\t{Math.Round(currentPackets / currentElapsed.TotalSeconds)}" +
                $"\tAverage RPS\t{Math.Round(totalRequests / totalElapsed.TotalSeconds)}" +
                $"\tConnections\t{_connections}");

            BenchmarksEventSource.Measure("tcpclient/requests", totalRequests);
            BenchmarksEventSource.Measure("tcpclient/max-rps", Math.Round(currentPackets / currentElapsed.TotalSeconds));
            BenchmarksEventSource.Measure("tcpclient/rps", Math.Round(totalRequests / totalElapsed.TotalSeconds));
        }
    }
}
