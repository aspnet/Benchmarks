using System.Collections.Concurrent;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Net.Security;
using System.Runtime;
using Microsoft.Crank.EventSources;

namespace HttpClientBenchmarks
{
    class Program
    {
        private static ClientOptions _options = null!;
        private static List<HttpClient> _httpClients = new();
        private static ConcurrentBag<long> _headersTimes = new();
        private static ConcurrentBag<long> _contentStartTimes = new();
        private static ConcurrentBag<long> _contentEndTimes = new();
        private static long _successRequests = 0;
        private static long _badStatusRequests = 0;
        private static long _exceptionRequests = 0;

        public static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand();
            rootCommand.AddOption(new Option<string>(new string[] { "--url" }, "The server url to request") { Required = true });
            rootCommand.AddOption(new Option<Version>(new string[] { "--httpVersion" }, "HTTP Version (1.1 or 2.0 or 3.0)") { Required = true });
            rootCommand.AddOption(new Option<int>(new string[] { "--numberOfClients" }, () => 10, "Number of HttpClients"));
            rootCommand.AddOption(new Option<int>(new string[] { "--concurrencyPerClient" }, () => 10, "Number of concurrect requests per one HttpClient"));
            rootCommand.AddOption(new Option<int>(new string[] { "--http11MaxConnectionsPerServer" }, () => 1, "Max number of HTTP/1.1 connections per server"));
            rootCommand.AddOption(new Option<bool>(new string[] { "--http20EnableMultipleConnections" }, () => false, "Enable multiple HTTP/2.0 connections"));
            rootCommand.AddOption(new Option<string>(new string[] { "--scenario" }, "Scenario to run") { Required = true });
            rootCommand.AddOption(new Option<int>(new string[] { "--warmup" }, () => 5, "Duration of the warmup in seconds"));
            rootCommand.AddOption(new Option<int>(new string[] { "--duration" }, () => 10, "Duration of the test in seconds"));

            rootCommand.Handler = CommandHandler.Create<ClientOptions>(async (options) =>
            {
                _options = options;
                Log("HttpClient benchmark");
                Log("Options: " + _options);

                Setup();
                Log("Setup done");

                await Warmup();
                Log("Warmup done");

                await RunScenario();
                Log("RunScenario done");
            });

            return await rootCommand.InvokeAsync(args);
        }

        private static void Setup()
        {
            BenchmarksEventSource.Register("IsServerGC", Operations.First, Operations.First, "Server GC enabled", "Server GC is enabled", "");
            BenchmarksEventSource.Measure("IsServerGC", GCSettings.IsServerGC.ToString());

            BenchmarksEventSource.Register("ProcessorCount", Operations.First, Operations.First, "Processor Count", "Processor Count", "n0");
            BenchmarksEventSource.Measure("ProcessorCount", Environment.ProcessorCount);

            for (int i = 0; i < _options.NumberOfClients; ++i)
            {
                var handler = new SocketsHttpHandler() 
                {
                    // accept all certs
                    SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = delegate { return true; } },
                    MaxConnectionsPerServer = _options.Http11MaxConnectionsPerServer,
                    EnableMultipleHttp2Connections = _options.Http20EnableMultipleConnections
                };
                _httpClients.Add(new HttpClient(handler)
                {
                    BaseAddress = new Uri(_options.Url!),
                    DefaultRequestVersion = _options.HttpVersion!,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
                });
            }
        }

        private static async Task Warmup()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.Warmup));
            var tasks = new List<Task>(_options.NumberOfClients);
            for (int i = 0; i < _options.NumberOfClients; ++i)
            {
                var client = _httpClients[i];
                tasks.Add(
                    Task.Run(async () =>
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                using var result = await client.GetAsync("/", cts.Token);
                                if (result.IsSuccessStatusCode)
                                {
                                    Interlocked.Increment(ref _successRequests);
                                }
                                else
                                {
                                    Log("Bad status code during warmup: " + result.StatusCode);
                                    Interlocked.Increment(ref _badStatusRequests);
                                }
                            }
                            catch (OperationCanceledException oce) when (oce.CancellationToken == cts.Token || cts.Token.IsCancellationRequested)
                            {
                                // ignore
                            }
                            catch (Exception ex)
                            {
                                Log("Exception during warmup: " + ex);
                                Interlocked.Increment(ref _exceptionRequests);
                            }
                        }
                    })
                );
            }
            await Task.WhenAll(tasks);
            BenchmarksEventSource.Log.Metadata("http/warmupsuccessrequests", "sum", "sum", "Warmup - Success Requests", "Number of successful requests during warmup", "n0");
            BenchmarksEventSource.Log.Metadata("http/warmupbadstatusrequests", "sum", "sum", "Warmup - Bad Status Code Requests", "Number of requests with bad status codes during warmup", "n0");
            BenchmarksEventSource.Log.Metadata("http/warmupexceptions", "sum", "sum", "Warmup - Exceptions", "Number of exceptions during warmup", "n0");
            BenchmarksEventSource.Measure("http/warmupsuccessrequests", _successRequests);
            BenchmarksEventSource.Measure("http/warmupbadstatusrequests", _badStatusRequests);
            BenchmarksEventSource.Measure("http/warmupexceptions", _exceptionRequests);
            _successRequests = 0;
            _badStatusRequests = 0;
            _exceptionRequests = 0;
        }

        private static async Task RunScenario()
        {
            BenchmarksEventSource.Log.Metadata("http/successrequests", "sum", "sum", "Success Requests", "Number of successful requests", "n0");
            BenchmarksEventSource.Log.Metadata("http/badstatusrequests", "sum", "sum", "Bad Status Code Requests", "Number of requests with bad status codes", "n0");
            BenchmarksEventSource.Log.Metadata("http/exceptions", "sum", "sum", "Exceptions", "Number of exceptions", "n0");
            BenchmarksEventSource.Log.Metadata("http/rps/mean", "avg", "avg", "Mean RPS", "Requests per second - mean", "n0");

            RegisterPercentiledMetric("http/headers", "Time to headers (ms)", "Time to headers (ms)");
            RegisterPercentiledMetric("http/contentstart", "Time to first content byte (ms)", "Time to first content byte (ms)");
            RegisterPercentiledMetric("http/contentend", "Time to last content byte (ms)", "Time to last content byte (ms)");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.Duration));
            var stopwatch = Stopwatch.StartNew();

            var tasks = new List<Task>(_options.NumberOfClients * _options.ConcurrencyPerClient);
            for (int i = 0; i < _options.NumberOfClients; ++i)
            {
                var client = _httpClients[i];
                for (int j = 0; j < _options.ConcurrencyPerClient; ++j)
                {
                    switch(_options.Scenario)
                    {
                        case "get":
                            tasks.Add(Get(client, cts.Token));
                            break;
                        default:
                            throw new ArgumentException($"Unknown scenario: {_options.Scenario}");
                    }
                }
            }

            await Task.WhenAll(tasks);
            var elapsed = stopwatch.Elapsed.TotalSeconds;
            BenchmarksEventSource.Measure("http/successrequests", _successRequests);
            BenchmarksEventSource.Measure("http/badstatusrequests", _badStatusRequests);
            BenchmarksEventSource.Measure("http/exceptions", _exceptionRequests);
            BenchmarksEventSource.Measure("http/rps/mean", (_successRequests + _badStatusRequests) / elapsed);

            if (_successRequests > 0)
            {
                LogPercentiledMetric("http/headers", _headersTimes, TicksToMs);
                LogPercentiledMetric("http/contentstart", _contentStartTimes, TicksToMs);
                LogPercentiledMetric("http/contentend", _contentEndTimes, TicksToMs);
            }
        }

        private static async Task Get(HttpClient client, CancellationToken token)
        {
            var oneByteArray = new byte[1];
            var drainArray = new byte[81920];
            var stopwatch = Stopwatch.StartNew();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    stopwatch.Restart();
                    using var result = await client.GetAsync("/get", HttpCompletionOption.ResponseHeadersRead, token);
                    var headersTime = stopwatch.ElapsedTicks;
                    if (result.IsSuccessStatusCode)
                    {
                        var content = await result.Content.ReadAsStreamAsync();
                        var bytesRead = await content.ReadAsync(oneByteArray, token);
                        var contentStartTime = stopwatch.ElapsedTicks;
                        while (bytesRead != 0)
                        {
                            bytesRead = await content.ReadAsync(drainArray, token);
                        }
                        var contentEndTime = stopwatch.ElapsedTicks;

                        _headersTimes.Add(headersTime);
                        _contentStartTimes.Add(contentStartTime);
                        _contentEndTimes.Add(contentEndTime);
                        Interlocked.Increment(ref _successRequests);
                    }
                    else
                    {
                        Log("Bad status code: " + result.StatusCode);
                        Interlocked.Increment(ref _badStatusRequests);
                    }
                }
                catch (OperationCanceledException oce) when (oce.CancellationToken == token || token.IsCancellationRequested)
                {
                    // ignore
                }
                catch (Exception ex)
                {
                    Log("Exception: " + ex);
                    Interlocked.Increment(ref _exceptionRequests);
                }
            }
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }

        private static double TicksToMs(double ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        private static double GetPercentile(int percent, long[] sortedArray)
        {
            if (percent == 0)
            {
                return sortedArray[0];
            }

            if (percent == 100)
            {
                return sortedArray[sortedArray.Length - 1];
            }

            var i = ((long)percent * sortedArray.Length) / 100.0 + 0.5;
            var fractionPart = i - Math.Truncate(i);

            return (1.0 - fractionPart) * sortedArray[(int)Math.Truncate(i) - 1] + fractionPart * sortedArray[(int)Math.Ceiling(i) - 1];
        }

        private static void RegisterPercentiledMetric(string name, string shortDescription, string longDescription)
        {
            BenchmarksEventSource.Log.Metadata(name + "/min", "min", "min", shortDescription + " - min", longDescription + " - min", "n2");
            BenchmarksEventSource.Log.Metadata(name + "/p50", "max", "max", shortDescription + " - p50", longDescription + " - 50th percentile", "n2");
            BenchmarksEventSource.Log.Metadata(name + "/p75", "max", "max", shortDescription + " - p75", longDescription + " - 75th percentile", "n2");
            BenchmarksEventSource.Log.Metadata(name + "/p90", "max", "max", shortDescription + " - p90", longDescription + " - 90th percentile", "n2");
            BenchmarksEventSource.Log.Metadata(name + "/p99", "max", "max", shortDescription + " - p99", longDescription + " - 99th percentile", "n2");
            BenchmarksEventSource.Log.Metadata(name + "/max", "max", "max", shortDescription + " - max", longDescription + " - max", "n2");
        }

        private static void LogPercentiledMetric(string name, ConcurrentBag<long> values, Func<double, double> prepareValue)
        {
            var sortedArray = values.ToArray();
            Array.Sort(sortedArray);

            BenchmarksEventSource.Measure(name + "/min", prepareValue(GetPercentile(0, sortedArray)));
            BenchmarksEventSource.Measure(name + "/p50", prepareValue(GetPercentile(50, sortedArray)));
            BenchmarksEventSource.Measure(name + "/p75", prepareValue(GetPercentile(75, sortedArray)));
            BenchmarksEventSource.Measure(name + "/p90", prepareValue(GetPercentile(90, sortedArray)));
            BenchmarksEventSource.Measure(name + "/p99", prepareValue(GetPercentile(99, sortedArray)));
            BenchmarksEventSource.Measure(name + "/max", prepareValue(GetPercentile(100, sortedArray)));
        }
    }
}
