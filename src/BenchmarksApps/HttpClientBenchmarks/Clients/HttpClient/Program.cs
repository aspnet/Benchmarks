using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Net.Security;
using Microsoft.Crank.EventSources;

namespace HttpClientBenchmarks;

class Program
{
    private static ClientOptions s_options = null!;
    private static List<HttpClient> s_httpClients = new();
    private static Metrics s_metrics = new();
    private static bool s_isWarmup;
    private static bool s_isRunning;

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand();
        rootCommand.AddOption(new Option<string>(new string[] { "--url" }, "The server url to request") { Required = true });
        rootCommand.AddOption(new Option<Version>(new string[] { "--httpVersion" }, "HTTP Version (1.1 or 2.0 or 3.0)") { Required = true });
        rootCommand.AddOption(new Option<int>(new string[] { "--numberOfHttpClients" }, () => 1, "Number of HttpClients"));
        rootCommand.AddOption(new Option<int>(new string[] { "--concurrencyPerHttpClient" }, () => 12, "Number of concurrect requests per one HttpClient"));
        rootCommand.AddOption(new Option<int>(new string[] { "--http11MaxConnectionsPerServer" }, () => 1, "Max number of HTTP/1.1 connections per server"));
        rootCommand.AddOption(new Option<bool>(new string[] { "--http20EnableMultipleConnections" }, () => false, "Enable multiple HTTP/2.0 connections"));
        rootCommand.AddOption(new Option<string>(new string[] { "--scenario" }, "Scenario to run") { Required = true });
        rootCommand.AddOption(new Option<int>(new string[] { "--warmup" }, () => 15, "Duration of the warmup in seconds"));
        rootCommand.AddOption(new Option<int>(new string[] { "--duration" }, () => 30, "Duration of the test in seconds"));

        rootCommand.Handler = CommandHandler.Create<ClientOptions>(async (options) =>
        {
            s_options = options;
            Log("HttpClient benchmark");
            Log("Options: " + s_options);

            await Setup();
            Log("Setup done");

            await RunScenario();
            Log("Scenario done");
        });

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task Setup()
    {
        BenchmarksEventSource.Register("ProcessorCount", Operations.First, Operations.First, "Processor Count", "Processor Count", "n0");
        LogMetric("ProcessorCount", Environment.ProcessorCount);

        for (int i = 0; i < s_options.NumberOfHttpClients; ++i)
        {
            var handler = new SocketsHttpHandler() 
            {
                // accept all certs
                SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = delegate { return true; } },
                MaxConnectionsPerServer = s_options.Http11MaxConnectionsPerServer > 0 ? s_options.Http11MaxConnectionsPerServer : int.MaxValue,
                EnableMultipleHttp2Connections = s_options.Http20EnableMultipleConnections
            };
            s_httpClients.Add(new HttpClient(handler)
            {
                DefaultRequestVersion = s_options.HttpVersion!,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
            });
        }

        var stopwatch = Stopwatch.StartNew();
        var response = await s_httpClients[0].GetAsync(new Uri(s_options.Url!));
        var elapsed = stopwatch.ElapsedMilliseconds;
        response.EnsureSuccessStatusCode();
        BenchmarksEventSource.Register("FirstRequest", Operations.Max, Operations.Max, "First request duration (ms)", "Duration of the first request to the server (ms)", "n0");
        LogMetric("FirstRequest", elapsed);
    }

    private static async Task RunScenario()
    {
        BenchmarksEventSource.Register("http/successrequests", Operations.Sum, Operations.Sum, "Success Requests", "Number of successful requests", "n0");
        BenchmarksEventSource.Register("http/badstatusrequests", Operations.Sum, Operations.Sum, "Bad Status Code Requests", "Number of requests with bad status codes", "n0");
        BenchmarksEventSource.Register("http/exceptions", Operations.Sum, Operations.Sum, "Exceptions", "Number of exceptions", "n0");
        BenchmarksEventSource.Register("http/rps/mean", Operations.Avg, Operations.Avg, "Mean RPS", "Requests per second - mean", "n0");

        RegisterPercentiledMetric("http/headers", "Time to headers (ms)", "Time to headers (ms)");
        RegisterPercentiledMetric("http/contentstart", "Time to first content byte (ms)", "Time to first content byte (ms)");
        RegisterPercentiledMetric("http/contentend", "Time to last content byte (ms)", "Time to last content byte (ms)");

        s_isWarmup = true;
        s_isRunning = true;

        var coordinatorTask = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(s_options.Warmup));
            s_isWarmup = false;
            Log("Completing warmup...");
            await Task.Delay(TimeSpan.FromSeconds(s_options.Duration));
            s_isRunning = false;
            Log("Completing scenario...");
        });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(s_options.Warmup + s_options.Duration) + TimeSpan.FromMinutes(10));
        var ctr = cts.Token.Register(() => throw new Exception("Test taking too long"));

        var tasks = new List<Task<Metrics>>(s_options.NumberOfHttpClients * s_options.ConcurrencyPerHttpClient);
        for (int i = 0; i < s_options.NumberOfHttpClients; ++i)
        {
            var client = s_httpClients[i];
            for (int j = 0; j < s_options.ConcurrencyPerHttpClient; ++j)
            {
                switch(s_options.Scenario)
                {
                    case "get":
                        tasks.Add(Get(client));
                        break;
                    default:
                        throw new ArgumentException($"Unknown scenario: {s_options.Scenario}");
                }
            }
        }

        var metricsArray = await Task.WhenAll(tasks);
        ctr.Dispose();
        foreach (var metrics in metricsArray)
        {
            s_metrics.Add(metrics);
        }

        LogMetric("http/successrequests", s_metrics.SuccessRequests);
        LogMetric("http/badstatusrequests", s_metrics.BadStatusRequests);
        LogMetric("http/exceptions", s_metrics.ExceptionRequests);
        LogMetric("http/rps/mean", s_metrics.MeanRps);

        if (s_metrics.SuccessRequests > 0)
        {
            LogPercentiledMetric("http/headers", s_metrics.HeadersTimes, TicksToMs);
            LogPercentiledMetric("http/contentstart", s_metrics.ContentStartTimes, TicksToMs);
            LogPercentiledMetric("http/contentend", s_metrics.ContentEndTimes, TicksToMs);
        }
    }

    private static async Task<Metrics> Get(HttpClient client)
    {
        var uri = new Uri(s_options.Url + "/get");

        var drainArray = new byte[81920];
        var stopwatch = Stopwatch.StartNew();
        
        var metrics = new Metrics();
        bool isWarmup = true;

        var durationStopwatch = Stopwatch.StartNew();
        while (s_isRunning)
        {
            if (isWarmup && !s_isWarmup)
            {
                if (metrics.SuccessRequests == 0)
                {
                    throw new Exception($"No successful requests during warmup.");
                }
                isWarmup = false;
                metrics.SuccessRequests = 0;
                metrics.BadStatusRequests = 0;
                metrics.ExceptionRequests = 0;
                durationStopwatch.Restart();
            }
            
            try
            {
                stopwatch.Restart();
                using var result = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                var headersTime = stopwatch.ElapsedTicks;
                if (result.IsSuccessStatusCode)
                {
                    var content = await result.Content.ReadAsStreamAsync();
                    var bytesRead = await content.ReadAsync(drainArray);
                    var contentStartTime = stopwatch.ElapsedTicks;
                    while (bytesRead != 0)
                    {
                        bytesRead = await content.ReadAsync(drainArray);
                    }
                    var contentEndTime = stopwatch.ElapsedTicks;

                    if (!isWarmup)
                    {
                        metrics.HeadersTimes.Add(headersTime);
                        metrics.ContentStartTimes.Add(contentStartTime);
                        metrics.ContentEndTimes.Add(contentEndTime);
                    }
                    metrics.SuccessRequests++;
                }
                else
                {
                    Log("Bad status code: " + result.StatusCode);
                    metrics.BadStatusRequests++;
                }
            }
            catch (Exception ex)
            {
                Log("Exception: " + ex);
                metrics.ExceptionRequests++;
            }
        }
        var elapsed = durationStopwatch.Elapsed.TotalSeconds;
        metrics.MeanRps = (metrics.SuccessRequests +  metrics.BadStatusRequests) / elapsed;
        return metrics;
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

    private static double GetPercentile(int percent, List<long> sortedValues)
    {
        if (percent == 0)
        {
            return sortedValues[0];
        }

        if (percent == 100)
        {
            return sortedValues[sortedValues.Count - 1];
        }

        var i = ((long)percent * sortedValues.Count) / 100.0 + 0.5;
        var fractionPart = i - Math.Truncate(i);

        return (1.0 - fractionPart) * sortedValues[(int)Math.Truncate(i) - 1] + fractionPart * sortedValues[(int)Math.Ceiling(i) - 1];
    }

    private static void RegisterPercentiledMetric(string name, string shortDescription, string longDescription)
    {
        BenchmarksEventSource.Register(name + "/min", Operations.Min, Operations.Min, shortDescription + " - min", longDescription + " - min", "n2");
        BenchmarksEventSource.Register(name + "/p75", Operations.Max, Operations.Max, shortDescription + " - p75", longDescription + " - 75th percentile", "n2");
        BenchmarksEventSource.Register(name + "/p50", Operations.Max, Operations.Max, shortDescription + " - p50", longDescription + " - 50th percentile", "n2");
        BenchmarksEventSource.Register(name + "/p90", Operations.Max, Operations.Max, shortDescription + " - p90", longDescription + " - 90th percentile", "n2");
        BenchmarksEventSource.Register(name + "/p99", Operations.Max, Operations.Max, shortDescription + " - p99", longDescription + " - 99th percentile", "n2");
        BenchmarksEventSource.Register(name + "/max", Operations.Max, Operations.Max, shortDescription + " - max", longDescription + " - max", "n2");
    }

    private static void LogPercentiledMetric(string name, List<long> values, Func<double, double> prepareValue)
    {
        values.Sort();

        LogMetric(name + "/min", prepareValue(GetPercentile(0, values)));
        LogMetric(name + "/p50", prepareValue(GetPercentile(50, values)));
        LogMetric(name + "/p75", prepareValue(GetPercentile(75, values)));
        LogMetric(name + "/p90", prepareValue(GetPercentile(90, values)));
        LogMetric(name + "/p99", prepareValue(GetPercentile(99, values)));
        LogMetric(name + "/max", prepareValue(GetPercentile(100, values)));
    }

    private static void LogMetric(string name, double value)
    {
        BenchmarksEventSource.Measure(name, value);
        Log($"{name}: {value}");
    }

    private static void LogMetric(string name, long value)
    {
        BenchmarksEventSource.Measure(name, value);
        Log($"{name}: {value}");
    }
}
