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
        private class ClientOptions
        {
            public string? Url { get; set; }
            public string? Scenario { get; set; }
        }

        private static ClientOptions _options = null!;
        private static HttpClient _httpClient = null!;

        public static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand();
            rootCommand.AddOption(new Option<string>(new string[] { "-u", "--url" }, "The server url to request") { Required = true });
            rootCommand.AddOption(new Option<string>(new string[] { "-s", "--scenario" }, "Scenario to run") { Required = true });

            rootCommand.Handler = CommandHandler.Create<ClientOptions>(async (options) =>
            {
                _options = options;
                Setup();
                await Warmup();

                await RunScenario();
            });

            return await rootCommand.InvokeAsync(args);
        }

        private static void Setup()
        {
            BenchmarksEventSource.Register("IsServerGC", Operations.First, Operations.First, "Server GC enabled", "Server GC is enabled", "");
            BenchmarksEventSource.Measure("IsServerGC", GCSettings.IsServerGC.ToString());

            BenchmarksEventSource.Register("ProcessorCount", Operations.First, Operations.First, "Processor Count", "Processor Count", "n0");
            BenchmarksEventSource.Measure("ProcessorCount", Environment.ProcessorCount);

            _httpClient = new HttpClient(new SocketsHttpHandler() 
            {
                // accept all certs
                SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = delegate { return true; } }
            });
        }

        private static async Task Warmup()
        {
            var stopwatch = new Stopwatch();

            stopwatch.Start();
            using var response = await _httpClient.GetAsync(_options.Url);
            var elapsed = stopwatch.ElapsedMilliseconds;

            BenchmarksEventSource.Log.Metadata("http/firstrequest", "max", "max", "First request (ms)", "First request (ms)", "n0");
            BenchmarksEventSource.Measure("http/firstrequest", elapsed);
        }

        private static async Task RunScenario()
        {
            if (_options.Scenario == "get")
            {
                BenchmarksEventSource.Log.Metadata("http/get", "max", "max", "GET (ms)", "GET (ms)", "n0");

                var stopwatch = Stopwatch.StartNew();

                for (int i = 0; i < 10; ++i)
                {
                    stopwatch.Restart();
                    using var response = await _httpClient.GetAsync(_options.Url + "/get");
                    var elapsed = stopwatch.ElapsedMilliseconds;
                    BenchmarksEventSource.Measure("http/get", elapsed);
                }
            }
        }
    }
}
