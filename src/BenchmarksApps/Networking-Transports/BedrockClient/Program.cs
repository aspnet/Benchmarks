using System.Diagnostics;
using System.Globalization;
using System.Net.Transport;
using Microsoft.Crank.EventSources;

// HTTP/1.1 keep-alive GET client. Runs --connections concurrent request loops over one SocketsHttpHandler.
// With --handler sockets the stock socket layer is used; any other handler plugs the Bedrock transport in through SocketsHttpHandler.ConnectCallback.
var arguments = Arguments.Parse(args);
var handlerName = arguments.Get("handler", "sockets");
var url = arguments.Get("url", "http://127.0.0.1:5000/1024");
var connections = arguments.Get("connections", 256);
var duration = TimeSpan.FromSeconds(arguments.Get("duration", 15));
var warmup = TimeSpan.FromSeconds(arguments.Get("warmup", 15));
var expected = arguments.Get("body-bytes", 1024);
var workers = arguments.Get("workers", 4);
var readAhead = arguments.Get("read-ahead", -1);

RegisterMetrics();
Console.WriteLine($"handler={handlerName} url={url} connections={connections} workers={workers} bodyBytes={expected} warmup={warmup.TotalSeconds}s duration={duration.TotalSeconds}s processors={Environment.ProcessorCount}");
Metric("env/processorcount", Environment.ProcessorCount);

NetworkTransportFactory? factory = null;
var handler = new SocketsHttpHandler { UseProxy = false, MaxConnectionsPerServer = int.MaxValue, PooledConnectionLifetime = Timeout.InfiniteTimeSpan };
if (handlerName != "sockets")
{
    factory = new NetworkTransportFactory(Backends.Create(handlerName, workers));
    if (readAhead < 0)
    {
        handler.UseConnectionFactory(factory);
    }
    else
    {
        handler.UseConnectionFactory(factory, readAhead);
    }
}
using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

var measuring = false;
var stop = false;
var errors = 0;
var badResponses = new long[connections];
var latencies = new List<int>[connections];
var tasks = new Task[connections];
for (var i = 0; i < connections; i++)
{
    var index = i;
    var samples = latencies[i] = new List<int>(1 << 16);
    tasks[i] = Task.Run(async () =>
    {
        var buffer = new byte[64 * 1024];
        while (!Volatile.Read(ref stop))
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                await using var body = await response.Content.ReadAsStreamAsync();
                var total = 0;
                int read;
                while ((read = await body.ReadAsync(buffer)) > 0)
                {
                    total += read;
                }
                var bad = !response.IsSuccessStatusCode || total != expected;
                if (Volatile.Read(ref measuring))
                {
                    samples.Add((int)Stopwatch.GetElapsedTime(started).TotalMicroseconds);
                    if (bad)
                    {
                        badResponses[index]++;
                    }
                }
            }
            catch (Exception error)
            {
                if (Interlocked.Increment(ref errors) == 1)
                {
                    Console.WriteLine($"first error: {error}");
                }
                await Task.Delay(10);
            }
        }
    });
}

await Task.Delay(warmup);
var clock = Stopwatch.StartNew();
Volatile.Write(ref measuring, true);
await Task.Delay(duration);
Volatile.Write(ref measuring, false);
clock.Stop();
Volatile.Write(ref stop, true);
await Task.WhenAll(tasks);

var all = new List<int>();
foreach (var samples in latencies)
{
    all.AddRange(samples);
}
all.Sort();
var requests = all.Count;
var rps = requests / clock.Elapsed.TotalSeconds;
var bads = badResponses.Sum();

Metric("http/requests", requests);
Metric("http/requests/badresponses", bads);
Metric("http/requests/errors", errors);
Metric("http/rps/mean", rps);
Metric("http/latency/mean", requests == 0 ? 0 : all.Average() / 1000.0);
Metric("http/latency/p50", Percentile(all, 50));
Metric("http/latency/p90", Percentile(all, 90));
Metric("http/latency/p99", Percentile(all, 99));
Metric("http/latency/max", Percentile(all, 100));
Console.WriteLine($"RESULT handler={handlerName} connections={connections} body={expected} requests={requests} rps={rps:F0} p50ms={Percentile(all, 50):F3} p99ms={Percentile(all, 99):F3} bad={bads} errors={errors}");

if (factory is not null)
{
    await factory.DisposeAsync();
}
return errors == 0 && bads == 0 ? 0 : 1;

static double Percentile(List<int> sorted, int percentile)
{
    if (sorted.Count == 0)
    {
        return 0;
    }
    var index = Math.Min(sorted.Count - 1, (int)(sorted.Count * (percentile / 100.0)));
    return sorted[index] / 1000.0;
}

static void Metric(string name, double value)
{
    BenchmarksEventSource.Measure(name, value);
    Console.WriteLine($"{name}: {value.ToString("F3", CultureInfo.InvariantCulture)}");
}

static void RegisterMetrics()
{
    BenchmarksEventSource.Register("env/processorcount", Operations.First, Operations.First, "Processor Count", "Processor Count", "n0");
    BenchmarksEventSource.Register("http/requests", Operations.Max, Operations.Sum, "Requests", "Total number of measured requests", "n0");
    BenchmarksEventSource.Register("http/requests/badresponses", Operations.Max, Operations.Sum, "Bad responses", "Non-2xx responses or unexpected body length", "n0");
    BenchmarksEventSource.Register("http/requests/errors", Operations.Max, Operations.Sum, "Errors", "Requests that threw", "n0");
    BenchmarksEventSource.Register("http/rps/mean", Operations.Max, Operations.Sum, "Mean RPS", "Requests per second during the measured window", "n0");
    BenchmarksEventSource.Register("http/latency/mean", Operations.Max, Operations.Avg, "Mean latency (ms)", "Mean latency", "n3");
    BenchmarksEventSource.Register("http/latency/p50", Operations.Max, Operations.Max, "p50 latency (ms)", "50th percentile latency", "n3");
    BenchmarksEventSource.Register("http/latency/p90", Operations.Max, Operations.Max, "p90 latency (ms)", "90th percentile latency", "n3");
    BenchmarksEventSource.Register("http/latency/p99", Operations.Max, Operations.Max, "p99 latency (ms)", "99th percentile latency", "n3");
    BenchmarksEventSource.Register("http/latency/max", Operations.Max, Operations.Max, "Max latency (ms)", "Maximum latency", "n3");
}
