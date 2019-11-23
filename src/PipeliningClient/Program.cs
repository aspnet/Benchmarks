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

        private static int _running;
        public static bool IsRunning => _running == 1;

        private static int _measuring;
        public static bool Measuring => _measuring == 1;

        public static string ServerUrl { get; set; }
        public static int PipelineDepth { get; set; }
        public static int WarmupTimeSeconds { get; set; }
        public static int ExecutionTimeSeconds { get; set; }
        public static int Connections { get; set; }
        public static List<string> Headers { get; set; }

        static async Task Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var optionUrl = app.Option("-u|--url <URL>", "The server url to request", CommandOptionType.SingleValue);
            var optionConnections = app.Option<int>("-c|--connections <N>", "Total number of HTTP connections to keep open", CommandOptionType.SingleValue);
            var optionWarmup = app.Option<int>("-w|--warmup <N>", "Duration of the warmup in seconds", CommandOptionType.SingleValue);
            var optionDuration = app.Option<int>("-d|--duration <N>", "Duration of the test in seconds", CommandOptionType.SingleValue);
            var optionHeaders = app.Option("-H|--header <HEADER>", "HTTP header to add to request, e.g. \"User-Agent: edge\"", CommandOptionType.MultipleValue);
            var optionPipeline = app.Option<int>("-p|--pipeline <N>", "The pipelining depth", CommandOptionType.SingleValue);

            app.OnExecuteAsync(cancellationToken =>
            {
                PipelineDepth = optionPipeline.HasValue()
                    ? int.Parse(optionPipeline.Value())
                    : 1;

                ServerUrl = optionUrl.Value();

                WarmupTimeSeconds = optionWarmup.HasValue()
                    ? int.Parse(optionWarmup.Value())
                    : 0;

                ExecutionTimeSeconds = int.Parse(optionDuration.Value());

                Connections = int.Parse(optionConnections.Value());

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

                        Interlocked.Exchange(ref _measuring, 1);

                        startTime = DateTime.UtcNow;
                    });

                // Shutdown everything
                yield return Task.Run(
                   async () =>
                   {
                       await Task.Delay(TimeSpan.FromSeconds(WarmupTimeSeconds + ExecutionTimeSeconds));

                       Console.WriteLine($"Stopping...");

                       Interlocked.Exchange(ref _running, 0);

                       stopTime = DateTime.UtcNow;
                   });

                foreach (var task in Enumerable.Range(0, Connections)
                    .Select(_ => Task.Run(DoWorkAsync)))
                {
                    yield return task;
                }
            }

            Interlocked.Exchange(ref _running, 1);

            await Task.WhenAll(CreateTasks());

            Console.WriteLine($"Stopped...");

            var totalTps = (int)(_counter / (stopTime - startTime).TotalSeconds);

            Console.WriteLine($"Average RPS:     {totalTps:N0}");
            Console.WriteLine($"2xx:             {_counter:N0}");
            Console.WriteLine($"Bad Responses:   {_errors:N0}");
            Console.WriteLine($"Socket Errors:   {_socketErrors:N0}");

            // If multiple samples are provided, take the max RPS, then sum the result from all clients
            BenchmarksEventSource.Log.Metadata("pipelineclient/avg-rps", "max", "sum", "RPS", "Requests per second", "n0");
            BenchmarksEventSource.Log.Measure("pipelineclient/avg-rps", totalTps);

            BenchmarksEventSource.Log.Metadata("pipelineclient/status-2xx", "sum", "sum", "Successful Requests", "Successful Requests", "n0");
            BenchmarksEventSource.Log.Measure("pipelineclient/status-2xx", _counter);

            BenchmarksEventSource.Log.Metadata("pipelineclient/bad-response", "sum", "sum", "Bad Responses", "Bad Responses", "n0");
            BenchmarksEventSource.Log.Measure("pipelineclient/bad-response", _errors);

            BenchmarksEventSource.Log.Metadata("pipelineclient/socket-errors", "sum", "sum", "Socket Errors", "Socket Errors", "n0");
            BenchmarksEventSource.Log.Measure("pipelineclient/socket-errors", _socketErrors);
        }

        public static async Task DoWorkAsync()
        {
            while (IsRunning)
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

                        while (IsRunning)
                        {
                            sw.Start();

                            var responses = await connection.SendRequestsAsync();

                            sw.Stop();
                            // Add the latency divided by the pipeline depth

                            var doBreak = false;
                            
                            for (var k = 0; k < responses.Length; k++ )
                            {
                                var response = responses[k];

                                if (Measuring)
                                {
                                    if (response.State == HttpResponseState.Completed)
                                    {
                                        if (response.StatusCode >= 200 && response.StatusCode < 300)
                                        {
                                            counter++;
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
