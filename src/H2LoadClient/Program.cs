using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Benchmarks;
using McMaster.Extensions.CommandLineUtils;

namespace H2LoadClient
{
    class Program
    {
        public static string ServerUrl { get; private set; }
        public static int Connections { get; private set; }
        public static int Threads { get; private set; }
        public static int Streams { get; private set; }
        public static int Timeout { get; private set; }
        public static int Warmup { get; private set; }
        public static int Duration { get; private set; }
        public static string Protocol { get; private set; }
        public static string RequestBodyFile { get; private set; }
        public static Dictionary<string, string> Headers { get; private set; }
        public static string Output { get; private set; }
        public static string Error { get; private set; }

        static async Task Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var optionUrl = app.Option("-u|--url <URL>", "The server url to request", CommandOptionType.SingleValue);
            var optionConnections = app.Option<int>("-c|--connections <N>", "Total number of connections to use", CommandOptionType.SingleValue);
            var optionThreads = app.Option<int>("-t|--threads <N>", "The number of native threads", CommandOptionType.SingleValue);
            var optionStreams = app.Option<int>("-m|--streams <N>", "Max concurrent streams to issue per session", CommandOptionType.SingleValue);
            var optionTimeout = app.Option<int>("-T|--timeout <N>", "Timeout to keep the connection open", CommandOptionType.SingleValue);
            var optionWarmup = app.Option<int>("-w|--warmup <N>", "Duration of the warmup in seconds", CommandOptionType.SingleValue);
            var optionDuration = app.Option<int>("-d|--duration <N>", "Duration of the test in seconds", CommandOptionType.SingleValue);
            var optionProtocol = app.Option<string>("-p|--protocol <S>", "The HTTP protocol to use", CommandOptionType.SingleValue);
            var optionBody = app.Option<string>("-b|--body <S>", "Request body as base64 encoded text", CommandOptionType.SingleValue);
            var optionHeaders = app.Option<string>("--header <S>", "Add a header to the request", CommandOptionType.MultipleValue);

            app.OnExecuteAsync(async cancellationToken =>
            {
                Console.WriteLine("H2Load Client");

                ServerUrl = optionUrl.Value();
                Connections = optionConnections.HasValue() ? optionConnections.ParsedValue : 1;
                Threads = optionThreads.HasValue() ? optionThreads.ParsedValue : 1;
                Streams = optionStreams.HasValue() ? optionStreams.ParsedValue : 1;
                Timeout = optionTimeout.HasValue() ? optionTimeout.ParsedValue : 5;
                Warmup = optionWarmup.HasValue() ? optionWarmup.ParsedValue : 5;
                Duration = optionDuration.HasValue() ? optionDuration.ParsedValue : 10;
                Protocol = optionProtocol.Value();

                Headers = new Dictionary<string, string>();
                foreach (var header in optionHeaders.ParsedValues)
                {
                    var headerParts = header.Split('=');
                    var key = headerParts[0];
                    var value = headerParts[1];
                    Headers.Add(key, value);

                    Console.WriteLine($"Header: {key}={value}");
                }

                if (Headers.Count == 0)
                {
                    Console.WriteLine("No headers");
                }

                if (optionBody.HasValue())
                {
                    var requestBody = Convert.FromBase64String(optionBody.Value());

                    // h2load takes a file as the request body
                    // write the body to a temporary file that is deleted in stop job
                    RequestBodyFile = Path.GetTempFileName();
                    await File.WriteAllBytesAsync(RequestBodyFile, requestBody);

                    Console.WriteLine($"Request body of {requestBody.Length} written to '{RequestBodyFile}'.");
                }

                var process = StartProcess();

                Console.WriteLine("Waiting for process exit");
                process.WaitForExit();

                // Wait for all Output messages to be flushed and available in Output
                await Task.Delay(100);

                Console.WriteLine("Parsing output");
                ParseOutput();
            });

            await app.ExecuteAsync(args);
        }

        private static void ParseOutput()
        {
            BenchmarksEventSource.Log.Metadata("h2load/requests", "max", "sum", "Requests", "Total number of requests", "n0");
            BenchmarksEventSource.Log.Metadata("h2load/badresponses", "max", "sum", "Bad responses", "Non-2xx or 3xx responses", "n0");
            BenchmarksEventSource.Log.Metadata("h2load/errors/socketerrors", "max", "sum", "Socket errors", "Socket errors", "n0");

            BenchmarksEventSource.Log.Metadata("h2load/latency/mean", "max", "sum", "Mean latency (us)", "Mean latency (us)", "n0");
            BenchmarksEventSource.Log.Metadata("h2load/latency/max", "max", "sum", "Max latency (us)", "Max latency (us)", "n0");

            BenchmarksEventSource.Log.Metadata("h2load/rps/max", "max", "sum", "Max RPS", "RPS: max", "n0");
            BenchmarksEventSource.Log.Metadata("h2load/raw", "all", "all", "Raw results", "Raw results", "object");

            double rps = 0;
            var rpsMatch = Regex.Match(Output, @"([\d\.]+) req/s");
            if (rpsMatch.Success && rpsMatch.Groups.Count == 2)
            {
                rps = double.Parse(rpsMatch.Groups[1].Value);
            }

            var latencyMatch = Regex.Match(Output, @"time for request: \s+[\d\.]+\w+\s+[\d\.]+\w+\s+([\d\.]+)(\w+)");
            var averageLatency = ReadLatency(latencyMatch);

            var p100Match = Regex.Match(Output, @"time for request: \s+[\d\.]+\w+\s+([\d\.]+)(\w+)");
            var maxLatency = ReadLatency(p100Match);

            var socketErrorsMatch = Regex.Match(Output, @"([\d\.]+) failed, ([\d\.]+) errored, ([\d\.]+) timeout");
            var socketErrors = CountSocketErrors(socketErrorsMatch);

            var badResponsesMatch = Regex.Match(Output, @"status codes: ([\d\.]+) 2xx, ([\d\.]+) 3xx, ([\d\.]+) 4xx, ([\d\.]+) 5xx");
            var badResponses = ReadBadReponses(badResponsesMatch);

            var requestsCountMatch = Regex.Match(Output, @"requests: ([\d\.]+) total");
            var totalRequests = ReadRequests(requestsCountMatch);

            BenchmarksEventSource.Measure("h2load/requests", totalRequests);
            BenchmarksEventSource.Measure("h2load/badresponses", badResponses);
            BenchmarksEventSource.Measure("h2load/errors/socketerrors", socketErrors);

            BenchmarksEventSource.Measure("h2load/latency/mean", averageLatency);
            BenchmarksEventSource.Measure("h2load/latency/max", maxLatency);

            BenchmarksEventSource.Measure("h2load/rps/max", rps);

            BenchmarksEventSource.Measure("h2load/raw", Output);
        }

        private static int ReadRequests(Match responseCountMatch)
        {
            if (!responseCountMatch.Success || responseCountMatch.Groups.Count != 2)
            {
                Log("Failed to parse requests");
                return -1;
            }

            try
            {
                return int.Parse(responseCountMatch.Groups[1].Value);
            }
            catch
            {
                Log("Failed to parse requests");
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

            if (!badResponsesMatch.Success || badResponsesMatch.Groups.Count != 5)
            {
                Log("Failed to parse bad responses");
                return 0;
            }

            try
            {
                return
                    int.Parse(badResponsesMatch.Groups[3].Value) +
                    int.Parse(badResponsesMatch.Groups[4].Value);
            }
            catch
            {
                Log("Failed to parse bad responses");
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

            if (socketErrorsMatch.Groups.Count != 4)
            {
                Log("Failed to parse socket errors");
                return 0;
            }

            try
            {
                return
                    int.Parse(socketErrorsMatch.Groups[1].Value) +
                    int.Parse(socketErrorsMatch.Groups[2].Value) +
                    int.Parse(socketErrorsMatch.Groups[3].Value);
            }
            catch
            {
                Log("Failed to parse socket errors");
                return 0;
            }
        }

        private static double ReadLatency(Match match)
        {
            if (!match.Success || match.Groups.Count != 3)
            {
                Log("Failed to parse latency");
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
                        Log("Failed to parse latency unit: " + unit);
                        return -1;
                }
            }
            catch
            {
                Log("Failed to parse latency");
                return -1;
            }
        }

        private static Process StartProcess()
        {
            var command = $"h2load {ServerUrl}";

            foreach (var header in Headers)
            {
                command += $" -H \"{header.Key}: {header.Value}\"";
            }

            command += $" -c {Connections} -T {Timeout} -t {Threads} -m {Streams} -D {Duration} --warm-up-time {Warmup}";

            switch (Protocol)
            {
                case "http": command += " --h1"; break;
                case "https": command += " --h1"; break;
                case "h2": break;
                case "h2c": command += " --no-tls-proto=h2c"; break;
                default: throw new InvalidOperationException("Unknown protocol: " + Protocol);
            }

            if (RequestBodyFile != null)
            {
                command += $" -d \"{RequestBodyFile}\"";
            }

            Log(command);

            var process = new Process()
            {
                StartInfo =
                {
                    FileName = "stdbuf",
                    Arguments = $"-oL {command}",
                    WorkingDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    Log(e.Data);
                    Output += (e.Data + Environment.NewLine);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    Log(e.Data);
                    Error += (e.Data + Environment.NewLine);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        private static void Log(string message)
        {
            var time = DateTime.Now.ToString("hh:mm:ss.fff");
            Console.WriteLine($"[{time}] {message}");
        }
    }
}
