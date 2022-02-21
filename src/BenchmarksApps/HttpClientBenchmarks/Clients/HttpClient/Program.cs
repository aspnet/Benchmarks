using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Runtime.InteropServices;
using Microsoft.Crank.EventSources;

namespace HttpClientBenchmarks;

class Program
{
    private static readonly double s_msPerTick = 1000.0 / Stopwatch.Frequency;

    private const int c_SingleThreadRpsEstimate = 3000;

    private static ClientOptions s_options = null!;
    private static Uri s_url = null!;
    private static List<HttpMessageInvoker> s_httpClients = new();
    private static Metrics s_metrics = new();

    private static byte[]? s_requestContentData = null;
    private static byte[]? s_requestContentLastChunk = null;
    private static int s_fullChunkCount;
    private static bool s_useByteArrayContent;

    private static bool s_isWarmup;
    private static bool s_isRunning;

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand();
        ClientOptionsBinder.AddOptionsToCommand(rootCommand);

        rootCommand.SetHandler(async (ClientOptions options) =>
            {
                s_options = options;
                Log("HttpClient benchmark");
                Log("Options: " + s_options);
                ValidateOptions();

                await Setup();
                Log("Setup done");

                await RunScenario();
                Log("Scenario done");
            },
            new ClientOptionsBinder());

        return await rootCommand.InvokeAsync(args);
    }

    private static void ValidateOptions()
    {
        if (!s_options.UseHttps && s_options.HttpVersion == HttpVersion.Version30)
        {
            throw new ArgumentException("HTTP/3.0 only supports HTTPS");
        }

        if (s_options.UseWinHttpHandler && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new ArgumentException("WinHttpHandler is only supported on Windows");
        }

        if (s_options.Scenario == "get" && s_options.ContentSize != 0)
        {
            throw new ArgumentException("Expected to have ContentSize = 0 for 'get' scenario, got " + s_options.ContentSize);
        }

        if (s_options.Scenario == "post" && s_options.ContentSize <= 0)
        {
            throw new ArgumentException("Expected to have ContentSize > 0 for 'post' scenario, got " + s_options.ContentSize);
        }
    }

    private static async Task Setup()
    {
        BenchmarksEventSource.Register("env/processorcount", Operations.First, Operations.First, "Processor Count", "Processor Count", "n0");
        LogMetric("env/processorcount", Environment.ProcessorCount);

        var baseUrl = $"http{(s_options.UseHttps ? "s" : "")}://{s_options.Address}:{s_options.Port}";
        s_url = new Uri(baseUrl + s_options.Path);
        Log("Base url: " + baseUrl);
        Log("Full url: " + s_url);
        
        int max11ConnectionsPerServer = s_options.Http11MaxConnectionsPerServer > 0 ? s_options.Http11MaxConnectionsPerServer : int.MaxValue;

        for (int i = 0; i < s_options.NumberOfHttpClients; ++i)
        {
            HttpMessageHandler handler;
            if (s_options.UseWinHttpHandler)
            {
                // Disable "only supported on: 'windows'" warning -- options are already validated
#pragma warning disable CA1416
                handler = new WinHttpHandler()
                {
                    // accept all certs
                    ServerCertificateValidationCallback = delegate { return true; },
                    MaxConnectionsPerServer = max11ConnectionsPerServer,
                    EnableMultipleHttp2Connections = s_options.Http20EnableMultipleConnections
                };
#pragma warning restore CA1416
            }
            else
            {
                handler = new SocketsHttpHandler() 
                {
                    // accept all certs
                    SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = delegate { return true; } },
                    MaxConnectionsPerServer = max11ConnectionsPerServer,
                    EnableMultipleHttp2Connections = s_options.Http20EnableMultipleConnections
                };
            }

            s_httpClients.Add(s_options.UseHttpMessageInvoker ? new HttpMessageInvoker(handler) : new HttpClient(handler));
        }

        if (s_options.ContentSize > 0)
        {
            CreateRequestContentData();
        }

        // First request to the server; to ensure everything started correctly
        var request = CreateRequest(HttpMethod.Get, new Uri(baseUrl));
        var stopwatch = Stopwatch.StartNew();
        var response = await SendAsync(s_httpClients[0], request);
        var elapsed = stopwatch.ElapsedMilliseconds;
        response.EnsureSuccessStatusCode();
        BenchmarksEventSource.Register("http/firstrequest", Operations.Max, Operations.Max, "First request duration (ms)", "Duration of the first request to the server (ms)", "n0");
        LogMetric("http/firstrequest", elapsed);
    }

    private static async Task RunScenario()
    {
        BenchmarksEventSource.Register("http/requests", Operations.Sum, Operations.Sum, "Requests", "Number of requests", "n0");
        BenchmarksEventSource.Register("http/requests/badresponses", Operations.Sum, Operations.Sum, "Bad Status Code Requests", "Number of requests with bad status codes", "n0");
        BenchmarksEventSource.Register("http/requests/errors", Operations.Sum, Operations.Sum, "Exceptions", "Number of exceptions", "n0");
        BenchmarksEventSource.Register("http/rps/mean", Operations.Avg, Operations.Avg, "Mean RPS", "Requests per second - mean", "n0");

        if (s_options.CollectRequestTimings)
        {
            RegisterPercentiledMetric("http/headers", "Time to response headers (ms)", "Time to response headers (ms)");
            RegisterPercentiledMetric("http/contentstart", "Time to first response content byte (ms)", "Time to first response content byte (ms)");
            RegisterPercentiledMetric("http/contentend", "Time to last response content byte (ms)", "Time to last response content byte (ms)");
        }

        Func<HttpMessageInvoker, Task<Metrics>> scenario;
        switch(s_options.Scenario)
        {
            case "get":
                scenario = Get;
                break;
            case "post":
                scenario = Post;
                break;
            default:
                throw new ArgumentException($"Unknown scenario: {s_options.Scenario}");
        }

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

        var tasks = new List<Task<Metrics>>(s_options.NumberOfHttpClients * s_options.ConcurrencyPerHttpClient);
        for (int i = 0; i < s_options.NumberOfHttpClients; ++i)
        {
            var client = s_httpClients[i];
            for (int j = 0; j < s_options.ConcurrencyPerHttpClient; ++j)
            {
                tasks.Add(scenario(client));
            }
        }

        await coordinatorTask;
        var metricsArray = await Task.WhenAll(tasks);
        foreach (var metrics in metricsArray)
        {
            s_metrics.Add(metrics);
        }

        LogMetric("http/requests", s_metrics.SuccessRequests + s_metrics.BadStatusRequests);
        LogMetric("http/requests/badresponses", s_metrics.BadStatusRequests);
        LogMetric("http/requests/errors", s_metrics.ExceptionRequests);
        LogMetric("http/rps/mean", s_metrics.MeanRps);

        if (s_options.CollectRequestTimings && s_metrics.SuccessRequests > 0)
        {
            LogPercentiledMetric("http/headers", s_metrics.HeadersTimes, TicksToMs);
            LogPercentiledMetric("http/contentstart", s_metrics.ContentStartTimes, TicksToMs);
            LogPercentiledMetric("http/contentend", s_metrics.ContentEndTimes, TicksToMs);
        }
    }

    private static Task<Metrics> Get(HttpMessageInvoker client)
    {
        return Measure(() => 
        {
            var request = CreateRequest(HttpMethod.Get, s_url);
            return SendAsync(client, request);
        });
    }

    private static Task<Metrics> Post(HttpMessageInvoker client)
    {
        return Measure(async () => 
        {
            var request = CreateRequest(HttpMethod.Post, s_url);

            Task<HttpResponseMessage> responseTask;
            if (s_useByteArrayContent)
            {
                request.Content = new ByteArrayContent(s_requestContentData!);
                responseTask = SendAsync(client, request);
            }
            else
            {
                var requestContent = new StreamingHttpContent();
                request.Content = requestContent;

                if (!s_options.ContentUnknownLength)
                {
                    request.Content.Headers.ContentLength = s_options.ContentSize;
                }
                // Otherwise, we don't need to set TransferEncodingChunked for HTTP/1.1 manually, it's done automatically if ContentLength is absent

                responseTask = SendAsync(client, request);
                var requestContentStream = await requestContent.GetStreamAsync();

                for (int i = 0; i < s_fullChunkCount; ++i)
                {
                    await requestContentStream.WriteAsync(s_requestContentData);
                    if (s_options.ContentFlushAfterWrite)
                    {
                        await requestContentStream.FlushAsync();
                    }
                }
                if (s_requestContentLastChunk != null)
                {
                    await requestContentStream.WriteAsync(s_requestContentLastChunk);
                }
                requestContent.CompleteStream();
            }

            return await responseTask;
        });
    }

    private static async Task<Metrics> Measure(Func<Task<HttpResponseMessage>> sendAsync)
    {
        var drainArray = new byte[ClientOptions.DefaultBufferSize];
        var stopwatch = Stopwatch.StartNew();

        var metrics = s_options.CollectRequestTimings 
            ? new Metrics(s_options.Duration * c_SingleThreadRpsEstimate) 
            : new Metrics();
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
                using HttpResponseMessage result = await sendAsync();
                var headersTime = stopwatch.ElapsedTicks;

                if (result.IsSuccessStatusCode)
                {
                    using var content = result.Content.ReadAsStream();
                    var bytesRead = await content.ReadAsync(drainArray);
                    var contentStartTime = stopwatch.ElapsedTicks;

                    while (bytesRead != 0)
                    {
                        bytesRead = await content.ReadAsync(drainArray);
                    }
                    var contentEndTime = stopwatch.ElapsedTicks;

                    if (s_options.CollectRequestTimings && !isWarmup)
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
        var elapsed = durationStopwatch.ElapsedTicks * 1.0 / Stopwatch.Frequency;
        metrics.MeanRps = (metrics.SuccessRequests +  metrics.BadStatusRequests) / elapsed;
        return metrics;
    }

    private static void CreateRequestContentData()
    {
        s_useByteArrayContent = !s_options.ContentUnknownLength && (!s_options.ContentFlushAfterWrite || s_options.ContentWriteSize >= s_options.ContentSize); // no streaming
        if (s_useByteArrayContent)
        {
            s_fullChunkCount = 1;
            s_requestContentData = new byte[s_options.ContentSize];
            Array.Fill(s_requestContentData, (byte)'a');
        }
        else
        {
            s_fullChunkCount = s_options.ContentSize / s_options.ContentWriteSize;
            if (s_fullChunkCount != 0)
            {
                s_requestContentData = new byte[s_options.ContentWriteSize];
                Array.Fill(s_requestContentData, (byte)'a');
            }
            int lastChunkSize = s_options.ContentSize % s_options.ContentWriteSize;
            if (lastChunkSize != 0)
            {
                s_requestContentLastChunk = new byte[lastChunkSize];
                Array.Fill(s_requestContentLastChunk, (byte)'a');
            }
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri) =>
        new HttpRequestMessage(method, uri) { Version = s_options.HttpVersion!, VersionPolicy = HttpVersionPolicy.RequestVersionExact };

    private static Task<HttpResponseMessage> SendAsync(HttpMessageInvoker client, HttpRequestMessage request) 
    {
        foreach (var header in s_options.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Name, header.Value))
            {
                throw new Exception($"Unable to add header \"{header.Name}: {header.Value}\" to the request");
            }
        }

        return s_options.UseHttpMessageInvoker
            ? client.SendAsync(request, CancellationToken.None)
            : ((HttpClient)client).SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private static void Log(string message)
    {
        var time = DateTime.UtcNow.ToString("hh:mm:ss.fff");
        Console.WriteLine($"[{time}] {message}");
    }

    private static double TicksToMs(double ticks) => ticks * s_msPerTick;

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
        BenchmarksEventSource.Register(name + "/p50", Operations.Max, Operations.Max, shortDescription + " - p50", longDescription + " - 50th percentile", "n2");
        BenchmarksEventSource.Register(name + "/p75", Operations.Max, Operations.Max, shortDescription + " - p75", longDescription + " - 75th percentile", "n2");
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
