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
        private static int _successRequests = 0;
        private static int _badStatusRequests = 0;
        private static int _exceptionRequests = 0;

        public static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand();
            rootCommand.AddOption(new Option<string>(new string[] { "-u", "--url" }, "The server url to request") { Required = true });
            rootCommand.AddOption(new Option<Version>(new string[] { "-h", "--httpVersion" }, "HTTP Version (1.1 or 2.0 or 3.0)") { Required = true });
            rootCommand.AddOption(new Option<int>(new string[] { "-n", "--numberOfClients" }, () => 10, "Number of HttpClients"));
            rootCommand.AddOption(new Option<int>(new string[] { "-nc", "--concurrencyPerClient" }, () => 10, "Number of concurrect requests per one HttpClient"));
            rootCommand.AddOption(new Option<int>(new string[] { "-h1mc", "--http11MaxConnectionsPerServer" }, () => 1, "Max number of HTTP/1.1 connections per server"));
            rootCommand.AddOption(new Option<bool>(new string[] { "-h2mc", "--http20EnableMultipleConnections" }, () => false, "Enable multiple HTTP/2.0 connections"));
            rootCommand.AddOption(new Option<string>(new string[] { "-s", "--scenario" }, "Scenario to run") { Required = true });
            rootCommand.AddOption(new Option<int>(new string[] { "-w", "--warmup" }, () => 5, "Duration of the warmup in seconds"));
            rootCommand.AddOption(new Option<int>(new string[] { "-d", "--duration" }, () => 10, "Duration of the test in seconds"));

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
                            catch (OperationCanceledException)
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

            BenchmarksEventSource.Log.Metadata("http/headersmax", "max", "max", "Time to headers (ms) - MAX", "Max time to headers (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("http/headersmin", "min", "min", "Time to headers (ms) - MIN", "Min time to headers (ms)", "n0");

            BenchmarksEventSource.Log.Metadata("http/contentstartmax", "max", "max", "Time to first content byte (ms) - MAX", "Max time to first content byte (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("http/contentstartmin", "min", "min", "Time to first content byte (ms) - MIN", "Min time to first content byte (ms)", "n0");

            BenchmarksEventSource.Log.Metadata("http/contentendmax", "max", "max", "Time to last content byte (ms) - MAX", "Max time to last content byte (ms)", "n0");
            BenchmarksEventSource.Log.Metadata("http/contentendmin", "min", "min", "Time to last content byte (ms) - MIN", "Min time to last content byte (ms)", "n0");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.Duration));

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
            BenchmarksEventSource.Measure("http/successrequests", _successRequests);
            BenchmarksEventSource.Measure("http/badstatusrequests", _badStatusRequests);
            BenchmarksEventSource.Measure("http/exceptions", _exceptionRequests);

            if (_successRequests > 0)
            {
                var headersTimes = _headersTimes.ToArray();
                var contentStartTimes = _contentStartTimes.ToArray();
                var contentEndTimes = _contentEndTimes.ToArray();
                Array.Sort(headersTimes);
                Array.Sort(contentStartTimes);
                Array.Sort(contentEndTimes);
                
                BenchmarksEventSource.Measure("http/headersmin", TicksToMs(headersTimes[0]));
                BenchmarksEventSource.Measure("http/contentstartmin", TicksToMs(contentStartTimes[0]));
                BenchmarksEventSource.Measure("http/contentendmin", TicksToMs(headersTimes[0]));
                BenchmarksEventSource.Measure("http/headersmax", TicksToMs(headersTimes[headersTimes.Length - 1]));
                BenchmarksEventSource.Measure("http/contentstartmax", TicksToMs(contentStartTimes[contentStartTimes.Length - 1]));
                BenchmarksEventSource.Measure("http/contentendmax", TicksToMs(contentEndTimes[contentEndTimes.Length - 1]));
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
                        var bytesRead = await content.ReadAsync(oneByteArray);
                        var contentStartTime = stopwatch.ElapsedTicks;
                        while (bytesRead != 0)
                        {
                            bytesRead = await content.ReadAsync(drainArray);
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
                catch (OperationCanceledException)
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

        private static long TicksToMs(long ticks)
        {
            return ticks * 1000 / Stopwatch.Frequency;
        }
    }
}
