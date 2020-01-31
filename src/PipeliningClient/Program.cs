using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace PipeliningClient
{
    class Program
    {
        private static object _synLock = new object();

        private static int _counter;
        private static int _errors;
        private static int _socketErrors;

        private static int _activeConnections;

        private static int _periodicCounter;

        private static bool _running;
        private static bool _measuring;

        public static string ServerUrl { get; set; }
        public static int PipelineDepth { get; set; }
        public static int WarmupTimeSeconds { get; set; }
        public static int ExecutionTimeSeconds { get; set; }
        public static int Connections { get; set; }
        public static int MinConnections { get; set; }
        public static int ConnectionRate { get; set; }
        public static double Period { get; set; }
        public static List<string> Headers { get; set; }

        private static List<KeyValuePair<int, int>> _statistics = new List<KeyValuePair<int, int>>();

        static async Task Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var optionUrl = app.Option("-u|--url <URL>", "The server url to request", CommandOptionType.SingleValue);
            var optionConnections = app.Option<int>("-c|--connections <N>", "Total number of HTTP connections to keep open", CommandOptionType.SingleValue);
            var optionPeriod = app.Option<double>("-i|--interval <F>", "Interval in seconds between connection increments. Default is 1. The value can be smaller than 1.", CommandOptionType.SingleValue);
            var optionMinConnections = app.Option<int>("-m|--min-connections <N>", "Total number of HTTP connections to start the warmup or the actual load. If not specified the number of connections is initially created.", CommandOptionType.SingleValue);
            var optionRateConnections = app.Option<int>("-r|--rate <N>", "Total number of HTTP connections to create during each Period, until the max number of connections is reached.", CommandOptionType.SingleValue);
            var optionWarmup = app.Option<int>("-w|--warmup <N>", "Duration of the warmup in seconds", CommandOptionType.SingleValue);
            var optionDuration = app.Option<int>("-d|--duration <N>", "Duration of the test in seconds", CommandOptionType.SingleValue);
            var optionHeaders = app.Option("-H|--header <HEADER>", "HTTP header to add to request, e.g. \"User-Agent: edge\"", CommandOptionType.MultipleValue);
            var optionPipeline = app.Option<int>("-p|--pipeline <N>", "The pipelining depth", CommandOptionType.SingleValue);

            app.OnExecuteAsync(cancellationToken =>
            {
                Console.WriteLine("Pipelining Client");

                PipelineDepth = optionPipeline.HasValue()
                    ? int.Parse(optionPipeline.Value())
                    : 1;

                ServerUrl = optionUrl.Value();

                WarmupTimeSeconds = optionWarmup.HasValue()
                    ? int.Parse(optionWarmup.Value())
                    : 0;

                ExecutionTimeSeconds = int.Parse(optionDuration.Value());

                Connections = int.Parse(optionConnections.Value());

                MinConnections = optionMinConnections.HasValue()
                    ? int.Parse(optionMinConnections.Value())
                    : Connections;

                _activeConnections = MinConnections;

                if (MinConnections > Connections)
                {
                    Console.WriteLine("The minimum number of connections can't be greater than the number of connections.");
                    app.ShowHelp();
                    return Task.CompletedTask;
                }

                Period = optionPeriod.HasValue()
                    ? double.Parse(optionPeriod.Value())
                    : 1;

                ConnectionRate = optionRateConnections.HasValue()
                    ? int.Parse(optionRateConnections.Value())
                    : (int) ((Connections - MinConnections) / Period)
                    ;
                
                
                if (Period < 0 || Period > WarmupTimeSeconds + ExecutionTimeSeconds)
                {
                    Console.WriteLine("Invalid period argument, resetting.");
                    Period = 1;
                }

                Headers = new List<string>(optionHeaders.Values);

                return RunAsync();
            });

            await app.ExecuteAsync(args);            
        }

        public static async Task RunAsync()
        {
            Console.WriteLine($"Running {ExecutionTimeSeconds}s test @ {ServerUrl}");

            DateTime startTime = default, stopTime = default;

            IEnumerable<Task> CreateTasks()
            {
                // Statistics thread
                yield return Task.Run(
                    async () =>
                    {
                        if (WarmupTimeSeconds > 0)
                        {
                            Console.WriteLine($"Warming up for {WarmupTimeSeconds}s...");
                            await Task.Delay(TimeSpan.FromSeconds(WarmupTimeSeconds));
                        }

                        Console.WriteLine($"Running for {ExecutionTimeSeconds}s...");

                        // Restart counters when measurement actually begins
                        lock (_synLock)
                        {
                            _counter = 0;
                            _errors = 0;
                            _socketErrors = 0;
                        }

                        _measuring = true;

                        startTime = DateTime.UtcNow;

                        var otherTasks = new List<Task>();

                        do
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Period));

                            var periodicTps = (int)(_periodicCounter / Period);
                            BenchmarksEventSource.Log.Measure("pipelineclient/periodic-rps", periodicTps);
                            BenchmarksEventSource.Log.Measure("pipelineclient/connections", _activeConnections);

                            _statistics.Add(new KeyValuePair<int, int>(_activeConnections, periodicTps));

                            lock (_synLock)
                            {
                                _periodicCounter = 0;
                            }

                            if (_running && _activeConnections < Connections)
                            {
                                var connectionsToCreate = _activeConnections + ConnectionRate <= Connections
                                    ? ConnectionRate
                                    : Connections - _activeConnections
                                    ;

                                _activeConnections += connectionsToCreate;

                                otherTasks.AddRange(Enumerable.Range(0, connectionsToCreate).Select(_ => Task.Run(DoWorkAsync)));
                            }

                        } while (_running);

                        await Task.WhenAll(otherTasks);

                    });

                // Shutdown everything
                yield return Task.Run(
                   async () =>
                   {
                       await Task.Delay(TimeSpan.FromSeconds(WarmupTimeSeconds + ExecutionTimeSeconds));

                       _running = false;

                       Console.WriteLine($"Stopping...");

                       stopTime = DateTime.UtcNow;
                   });

                // Create as many as the --min-connections argument
                foreach (var task in Enumerable.Range(0, MinConnections)
                    .Select(_ => Task.Run(DoWorkAsync)))
                {
                    yield return task;
                }

            }

            _running = true;

            await Task.WhenAll(CreateTasks());

            Console.WriteLine($"Stopped...");

            var totalTps = (int)(_counter / (stopTime - startTime).TotalSeconds);

            Console.WriteLine($"Average RPS:     {totalTps:N0}");
            Console.WriteLine($"2xx:             {_counter:N0}");
            Console.WriteLine($"Bad Responses:   {_errors:N0}");
            Console.WriteLine($"Socket Errors:   {_socketErrors:N0}");

            // If multiple samples are provided, take the max RPS, then sum the result from all clients
            BenchmarksEventSource.Log.Metadata("pipelineclient/avg-rps", "max", "sum", "RPS", "Requests per second", "n0");
            BenchmarksEventSource.Log.Metadata("pipelineclient/connections", "max", "sum", "Connections", "Number of active connections", "n0");
            BenchmarksEventSource.Log.Metadata("pipelineclient/periodic-rps", "max", "sum", "Max RPS", "Instant requests per second", "n0");
            BenchmarksEventSource.Log.Metadata("pipelineclient/status-2xx", "sum", "sum", "Successful Requests", "Successful Requests", "n0");
            BenchmarksEventSource.Log.Metadata("pipelineclient/bad-response", "sum", "sum", "Bad Responses", "Bad Responses", "n0");
            BenchmarksEventSource.Log.Metadata("pipelineclient/socket-errors", "sum", "sum", "Socket Errors", "Socket Errors", "n0");

            BenchmarksEventSource.Log.Measure("pipelineclient/avg-rps", totalTps);
            BenchmarksEventSource.Log.Measure("pipelineclient/connections", Connections);
            BenchmarksEventSource.Log.Measure("pipelineclient/status-2xx", _counter);
            BenchmarksEventSource.Log.Measure("pipelineclient/bad-response", _errors);
            BenchmarksEventSource.Log.Measure("pipelineclient/socket-errors", _socketErrors);

            if (MinConnections != Connections)
            {
                Console.WriteLine();
                Console.WriteLine($"| Connections | {"RPS ".PadRight(11, ' ')} |");
                Console.WriteLine($"| ----------- | ----------- |");
                foreach (var entry in _statistics)
                {
                    Console.WriteLine($"| {entry.Key.ToString().PadRight(11)} | {entry.Value.ToString().PadRight(11)} |");
                }
            }
        }

        public static async Task DoWorkAsync()
        {
            while (_running)
            {
                // Creating a new connection every time it is necessary
                using (var connection = new HttpConnection(ServerUrl, PipelineDepth, Headers))
                {
                    await connection.ConnectAsync();

                    // Counters local to this connection
                    var counter = 0;
                    var errors = 0;
                    var socketErrors = 0;

                    try
                    {
                        var sw = new Stopwatch();

                        while (_running)
                        {
                            sw.Start();

                            var responses = await connection.SendRequestsAsync();

                            sw.Stop();
                            // Add the latency divided by the pipeline depth

                            var doBreak = false;
                            
                            for (var k = 0; k < responses.Length; k++ )
                            {
                                var response = responses[k];

                                if (_measuring)
                                {
                                    if (response.State == HttpResponseState.Completed)
                                    {
                                        if (response.StatusCode >= 200 && response.StatusCode < 300)
                                        {
                                            counter++;
                                            _periodicCounter++;
                                        }
                                        else
                                        {
                                            errors++;
                                        }
                                    }
                                    else
                                    {
                                        socketErrors++;
                                        doBreak = true;
                                    }
                                }
                            }

                            if (doBreak)
                            {
                                break;
                            }
                        }
                    }
                    catch
                    {
                        socketErrors++;
                    }
                    finally
                    {
                        // Update the global counters when the connections is ending
                        lock (_synLock)
                        {
                            _counter += counter;
                            _errors += errors;
                            _socketErrors += socketErrors;
                        }
                    }
                }
            }
        }
    }
}
