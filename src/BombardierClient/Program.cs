using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Benchmarks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BombardierClient
{
    class Program
    {
        private static readonly HttpClient _httpClient;
        private static readonly HttpClientHandler _httpClientHandler;

        private static Dictionary<PlatformID, string> _bombardierUrls = new Dictionary<PlatformID, string>()
        {
            { PlatformID.Win32NT, "https://github.com/codesenberg/bombardier/releases/download/v1.2.4/bombardier-windows-amd64.exe" },
            { PlatformID.Unix, "https://github.com/codesenberg/bombardier/releases/download/v1.2.4/bombardier-linux-amd64" },
        };

        static Program()
        {
            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClientHandler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;

            _httpClient = new HttpClient(_httpClientHandler);
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine("Bombardier Client");
            Console.WriteLine("args: " + String.Join(' ', args));

            var bombardierUrl = _bombardierUrls[Environment.OSVersion.Platform];
            var bombardierFileName = Path.GetFileName(bombardierUrl);

            using (var downloadStream = await _httpClient.GetStreamAsync(bombardierUrl))
            using (var fileStream = File.Create(bombardierFileName))
            {
                await downloadStream.CopyToAsync(fileStream);
                if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    Process.Start("chmod", "+x " + bombardierFileName);
                }
            }


            var process = new Process()
            {
                StartInfo = {
                    FileName = bombardierFileName,
                    Arguments = String.Join(' ', args) + " --print r --format json",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
                EnableRaisingEvents = true
            };

            var stringBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e != null && e.Data != null)
                {
                    stringBuilder.Append(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();

            Console.WriteLine(stringBuilder);

            var document = JObject.Parse(stringBuilder.ToString());

            BenchmarksEventSource.Log.Metadata("bombardier/req1xx", "max", "sum", "1xx", "Requests with 1xx status code", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/req2xx", "max", "sum", "2xx", "Requests with 2xx status code", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/req3xx", "max", "sum", "3xx", "Requests with 3xx status code", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/req4xx", "max", "sum", "4xx", "Requests with 4xx status code", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/req5xx", "max", "sum", "5xx", "Requests with 5xx status code", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/others", "max", "sum", "others", "Requests with other status code", "n0");

            BenchmarksEventSource.Log.Metadata("bombardier/latency/mean", "max", "sum", "Mean latency", "Latency: mean", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/latency/max", "max", "sum", "Max latency", "Latency: max", "n0");

            BenchmarksEventSource.Log.Metadata("bombardier/rps/mean", "max", "sum", "Mean RPS", "RPS: mean", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/rps/max", "max", "sum", "Max RPS", "RPS: max", "n0");

            BenchmarksEventSource.Log.Metadata("bombardier/rps/percentile/50", "max", "sum", "RPS (50th)", "50th percentile RPS", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/rps/percentile/75", "max", "sum", "RPS (75th)", "75th percentile RPS", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/rps/percentile/90", "max", "sum", "RPS (90th)", "90th percentile RPS", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/rps/percentile/95", "max", "sum", "RPS (95th)", "95th percentile RPS", "n0");
            BenchmarksEventSource.Log.Metadata("bombardier/rps/percentile/99", "max", "sum", "RPS (99th)", "99th percentile RPS", "n0");

            BenchmarksEventSource.Measure("bombardier/req1xx", document["result"]["req1xx"].Value<int>());
            BenchmarksEventSource.Measure("bombardier/req2xx", document["result"]["req2xx"].Value<int>());
            BenchmarksEventSource.Measure("bombardier/req3xx", document["result"]["req3xx"].Value<int>());
            BenchmarksEventSource.Measure("bombardier/req4xx", document["result"]["req4xx"].Value<int>());
            BenchmarksEventSource.Measure("bombardier/req5xx", document["result"]["req5xx"].Value<int>());
            BenchmarksEventSource.Measure("bombardier/others", document["result"]["others"].Value<int>());

            BenchmarksEventSource.Measure("bombardier/latency/mean", document["result"]["latency"]["mean"].Value<double>());
            BenchmarksEventSource.Measure("bombardier/latency/stddev", document["result"]["latency"]["stddev"].Value<double>());
            BenchmarksEventSource.Measure("bombardier/latency/max", document["result"]["latency"]["max"].Value<double>());

            BenchmarksEventSource.Measure("bombardier/rps/mean", document["result"]["rps"]["mean"].Value<double>());
            BenchmarksEventSource.Measure("bombardier/rps/stddev", document["result"]["rps"]["stddev"].Value<double>());
            BenchmarksEventSource.Measure("bombardier/rps/max", document["result"]["rps"]["max"].Value<double>());

            BenchmarksEventSource.Measure("bombardier/rps/percentile/50", document["result"]["rps"]["percentiles"]["50"].Value<double>());
            BenchmarksEventSource.Measure("bombardier/rps/percentile/75", document["result"]["rps"]["percentiles"]["75"].Value<double>());
            BenchmarksEventSource.Measure("bombardier/rps/percentile/90", document["result"]["rps"]["percentiles"]["90"].Value<double>());
            BenchmarksEventSource.Measure("bombardier/rps/percentile/95", document["result"]["rps"]["percentiles"]["95"].Value<double>());
            BenchmarksEventSource.Measure("bombardier/rps/percentile/99", document["result"]["rps"]["percentiles"]["99"].Value<double>());

        }
    }
}
