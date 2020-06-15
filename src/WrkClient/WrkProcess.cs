using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Wrk2Client;

namespace WrkClient
{
    static class WrkProcess
    {
        public static async Task MeasureFirstRequest(string[] args)
        {
            var url = args.FirstOrDefault(arg => arg.StartsWith("http", StringComparison.OrdinalIgnoreCase));

            if (url == null)
            {
                Console.WriteLine("URL not found, skipping first request");
                return;
            }

            // Configuring the http client to trust the self-signed certificate
            var httpClientHandler = new HttpClientHandler();
            httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            httpClientHandler.MaxConnectionsPerServer = 1;
            using(var httpClient = new HttpClient(httpClientHandler))
            {
                var cts = new CancellationTokenSource(5000);
                var httpMessage = new HttpRequestMessage(HttpMethod.Get, url);

                var stopwatch = new Stopwatch();
                stopwatch.Start();

                try
                {
                    using (var response = await httpClient.SendAsync(httpMessage, cts.Token))
                    {
                        var elapsed = stopwatch.ElapsedMilliseconds;
                        Console.WriteLine($"{elapsed} ms");

                        BenchmarksEventSource.Log.Metadata("http/firstrequest", "max", "avg", "First request (ms)", "First request (ms)", "n0");
                        BenchmarksEventSource.Measure("http/firstrequest", elapsed);
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("A timeout occurred while measuring the first request");
                }
            }
        }

        public static async Task RunAsync(string fileName, string[] args)
        {
            // Do we need to parse latency?
            var parseLatency = args.Any(x => x == "--latency" || x == "-L");
            var tempScriptFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            try
            {
                await ProcessScriptFile(args, tempScriptFile);
                RunCore(fileName, args, parseLatency);
            }
            finally
            {
                if (File.Exists(tempScriptFile))
                {
                    File.Delete(tempScriptFile);
                }
            }
        }

        static async Task ProcessScriptFile(string[] args, string tempScriptFile)
        {
            using var httpClient = new HttpClient();

            for (var i = 0; i < args.Length - 1; i++)
            {
                // wrk does not support loading scripts from the network. We'll shim it in this client.
                if ((args[i] == "-s" || args[i] == "--script") &&
                    Uri.TryCreate(args[i + 1], UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "http" || uri.Scheme == "https"))
                {
                    using var response = await httpClient.GetStreamAsync(uri);

                    using var fileStream = File.Create(tempScriptFile);
                    await response.CopyToAsync(fileStream);

                    args[i + 1] = tempScriptFile;
                }
            }
        }

        static void RunCore(string fileName, string[] args, bool parseLatency)
        {
            // Extracting duration parameters
            string warmup = "";
            string duration = "";

            var argsList = args.ToList();

            var durationIndex = argsList.FindIndex(x => String.Equals(x, "-d", StringComparison.OrdinalIgnoreCase));
            if (durationIndex >= 0)
            {
                duration = argsList[durationIndex + 1];
                argsList.RemoveAt(durationIndex);
                argsList.RemoveAt(durationIndex);
            }
            else
            {
                Console.WriteLine("Couldn't find -d argument");
                return;
            }

            var warmupIndex = argsList.FindIndex(x => String.Equals(x, "-w", StringComparison.OrdinalIgnoreCase));
            if (warmupIndex >= 0)
            {
                warmup = argsList[warmupIndex + 1];
                argsList.RemoveAt(warmupIndex);
                argsList.RemoveAt(warmupIndex);
            }

            args = argsList.ToArray();

            var baseArguments = String.Join(' ', args.ToArray());

            var process = new Process()
            {
                StartInfo = 
                {
                    FileName = fileName,
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
                    Console.WriteLine(e.Data);

                    lock (stringBuilder)
                    {
                        stringBuilder.AppendLine(e.Data);
                    }
                }
            };

            // Warmup

            if (!String.IsNullOrEmpty(warmup) && warmup != "0s")
            {
                process.StartInfo.Arguments = $"-d {warmup} {baseArguments}";

                Console.WriteLine("> wrk " + process.StartInfo.Arguments);

                process.Start();
                process.WaitForExit();
            }

            lock (stringBuilder)
            {
                stringBuilder.Clear();
            }

            process.StartInfo.Arguments = $"-d {duration} {baseArguments}";

            Console.WriteLine("> wrk " + process.StartInfo.Arguments);

            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();

            string output;

            lock (stringBuilder)
            {
                output = stringBuilder.ToString();
            }

            BenchmarksEventSource.Log.Metadata("wrk/rps/mean", "max", "sum", "Requests/sec", "Requests per second", "n0");
            BenchmarksEventSource.Log.Metadata("wrk/requests", "max", "sum", "Requests", "Total number of requests", "n0");
            BenchmarksEventSource.Log.Metadata("wrk/latency/mean", "max", "sum", "Mean latency (ms)", "Mean latency (ms)", "n2");
            BenchmarksEventSource.Log.Metadata("wrk/latency/max", "max", "sum", "Max latency (ms)", "Max latency (ms)", "n2");
            BenchmarksEventSource.Log.Metadata("wrk/errors/badresponses", "max", "sum", "Bad responses", "Non-2xx or 3xx responses", "n0");
            BenchmarksEventSource.Log.Metadata("wrk/errors/socketerrors", "max", "sum", "Socket errors", "Socket errors", "n0");

            const string LatencyPattern = @"\s+{0}\s+([\d\.]+)(\w+)";

            var latencyMatch = Regex.Match(output, String.Format(LatencyPattern, "Latency"));
            BenchmarksEventSource.Measure("wrk/latency/mean", ReadLatency(latencyMatch));

            var rpsMatch = Regex.Match(output, @"Requests/sec:\s*([\d\.]*)");
            if (rpsMatch.Success && rpsMatch.Groups.Count == 2)
            {
                BenchmarksEventSource.Measure("wrk/rps/mean", double.Parse(rpsMatch.Groups[1].Value));
            }

            // Max latency is 3rd number after "Latency "
            var maxLatencyMatch = Regex.Match(output, @"\s+Latency\s+[\d\.]+\w+\s+[\d\.]+\w+\s+([\d\.]+)(\w+)");
            BenchmarksEventSource.Measure("wrk/latency/max", ReadLatency(maxLatencyMatch));

            var requestsCountMatch = Regex.Match(output, @"([\d\.]*) requests in ([\d\.]*)(\w*)");
            BenchmarksEventSource.Measure("wrk/requests", ReadRequests(requestsCountMatch));

            var badResponsesMatch = Regex.Match(output, @"Non-2xx or 3xx responses: ([\d\.]*)");
            BenchmarksEventSource.Measure("wrk/errors/badresponses", ReadBadReponses(badResponsesMatch));

            var socketErrorsMatch = Regex.Match(output, @"Socket errors: connect ([\d\.]*), read ([\d\.]*), write ([\d\.]*), timeout ([\d\.]*)");
            BenchmarksEventSource.Measure("wrk/errors/socketerrors", CountSocketErrors(socketErrorsMatch));

            if (parseLatency)
            {
                BenchmarksEventSource.Log.Metadata("wrk/latency/50", "max", "avg", "Latency 50th (ms)", "Latency 50th (ms)", "n2");
                BenchmarksEventSource.Log.Metadata("wrk/latency/75", "max", "avg", "Latency 75th (ms)", "Latency 50th (ms)", "n2");
                BenchmarksEventSource.Log.Metadata("wrk/latency/90", "max", "avg", "Latency 90th (ms)", "Latency 50th (ms)", "n2");
                BenchmarksEventSource.Log.Metadata("wrk/latency/99", "max", "avg", "Latency 99th (ms)", "Latency 50th (ms)", "n2");

                BenchmarksEventSource.Measure("wrk/latency/50", ReadLatency(Regex.Match(output, string.Format(LatencyPattern, "50%"))));
                BenchmarksEventSource.Measure("wrk/latency/75", ReadLatency(Regex.Match(output, string.Format(LatencyPattern, "75%"))));
                BenchmarksEventSource.Measure("wrk/latency/90", ReadLatency(Regex.Match(output, string.Format(LatencyPattern, "90%"))));
                BenchmarksEventSource.Measure("wrk/latency/99", ReadLatency(Regex.Match(output, string.Format(LatencyPattern, "99%"))));
            }
        }

        private static int ReadRequests(Match responseCountMatch)
        {
            if (!responseCountMatch.Success || responseCountMatch.Groups.Count != 4)
            {
                Console.WriteLine("Failed to parse requests");
                return -1;
            }

            try
            {
                return int.Parse(responseCountMatch.Groups[1].Value);
            }
            catch
            {
                Console.WriteLine("Failed to parse requests");
                return -1;
            }
        }

        private static int ReadBadReponses(Match badResponsesMatch)
        {
            if (!badResponsesMatch.Success)
            {
                // wrk does not display the expected line when no bad responses occur
                return 0;
            }

            if (!badResponsesMatch.Success || badResponsesMatch.Groups.Count != 2)
            {
                Console.WriteLine("Failed to parse bad responses");
                return 0;
            }

            try
            {
                return int.Parse(badResponsesMatch.Groups[1].Value);
            }
            catch
            {
                Console.WriteLine("Failed to parse bad responses");
                return 0;
            }
        }

        private static int CountSocketErrors(Match socketErrorsMatch)
        {
            if (!socketErrorsMatch.Success)
            {
                // wrk does not display the expected line when no errors occur
                return 0;
            }

            if (socketErrorsMatch.Groups.Count != 5)
            {
                Console.WriteLine("Failed to parse socket errors");
                return 0;
            }

            try
            {
                return
                    int.Parse(socketErrorsMatch.Groups[1].Value) +
                    int.Parse(socketErrorsMatch.Groups[2].Value) +
                    int.Parse(socketErrorsMatch.Groups[3].Value) +
                    int.Parse(socketErrorsMatch.Groups[4].Value)
                    ;

            }
            catch
            {
                Console.WriteLine("Failed to parse socket errors");
                return 0;
            }

        }

        private static double ReadLatency(Match match)
        {
            if (!match.Success || match.Groups.Count != 3)
            {
                Console.WriteLine("Failed to parse latency");
                return -1;
            }

            try
            {
                var value = double.Parse(match.Groups[1].Value);
                var unit = match.Groups[2].Value;

                switch (unit)
                {
                    case "s": return value * 1000;
                    case "ms": return value;
                    case "us": return value / 1000;

                    default:
                        Console.WriteLine("Failed to parse latency unit: " + unit);
                        return -1;
                }
            }
            catch
            {
                Console.WriteLine("Failed to parse latency");
                return -1;
            }
        }
    }
}
