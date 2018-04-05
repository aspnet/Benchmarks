// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace HttpBenchmark
{
    public class Program
    {
        private static Stopwatch _stopwatch = new Stopwatch();
        private static long _requests;
        private static long _connections;

        private const int _defaultConnections = 256;
        private const int _defaultDuration = 10;
        private const int _defaultPipeline = 1;
        private const bool _defaultKeepalive = true;
        private const string _defaultMethod = "GET";

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                Name = "HttpBenchmark",
                FullName = "HTTP Benchmark",
                Description = "HTTP benchmarking tool"
            };

            app.HelpOption("-?|-h|--help");

            var connectionsOption = app.Option("-c|--connections",
                $"Number of connections.  Default is {_defaultConnections}.",
                CommandOptionType.SingleValue);
            var durationOption = app.Option("-d|--duration",
                $"Duration of test in seconds.  Default is {_defaultDuration}.",
                CommandOptionType.SingleValue);
            var pipelineOption = app.Option("-p|--pipeline",
                $"Number of HTTP requests to pipeline in a single network roundtrip.  Default is {_defaultPipeline} (no pipelining).",
                CommandOptionType.SingleValue);
            var keepaliveOption = app.Option("-k|--keepalive",
                $"Reuse connections for multiple requests.  Default is {_defaultKeepalive}.",
                CommandOptionType.SingleValue);
            var methodOption = app.Option("-m|--method",
                $"Request method.  Default is {_defaultMethod}.",
                CommandOptionType.SingleValue);
            var bodyOption = app.Option("-b|--body", "Request body.", CommandOptionType.SingleValue);

            var urlArgument = app.Argument("url", "URL to benchmark");

            app.OnExecute(() =>
            {
                try
                {
                    var connections = _defaultConnections;
                    if (connectionsOption.HasValue())
                    {
                        connections = int.Parse(connectionsOption.Value());
                    }

                    var duration = _defaultDuration;
                    if (durationOption.HasValue())
                    {
                        duration = int.Parse(durationOption.Value());
                    }

                    var pipeline = _defaultPipeline;
                    if (pipelineOption.HasValue())
                    {
                        pipeline = int.Parse(pipelineOption.Value());
                    }

                    var keepalive = _defaultKeepalive;
                    if (keepaliveOption.HasValue())
                    {
                        keepalive = bool.Parse(keepaliveOption.Value());
                    }

                    var method = _defaultMethod;
                    if (methodOption.HasValue())
                    {
                        method = methodOption.Value();
                    }
                    method = method.ToUpperInvariant();

                    var body = bodyOption.Value();

                    Uri uri = null;
                    if (!string.IsNullOrWhiteSpace(urlArgument.Value))
                    {
                        uri = new Uri(urlArgument.Value);
                    }

                    if (connections <= 0 ||
                        duration <= 0 ||
                        pipeline <= 0 ||
                        uri == null)
                    {
                        app.ShowHelp();
                        return 2;
                    }

                    return Run(connections, TimeSpan.FromSeconds(duration), pipeline, keepalive, method, body, uri);
                }
                catch (Exception e)
                {
                    app.ShowHelp();
                    Console.WriteLine(e);
                    return 2;
                }
            });

            return app.Execute(args);
        }

        private static int Run(int connections, TimeSpan duration, int pipeline, bool keepalive, string method, string body, Uri uri)
        {
            Init(connections, duration, pipeline, keepalive, uri);

            var cancellationToken = (new CancellationTokenSource(duration)).Token;
            var writeResultsTask = WriteResults(cancellationToken);

            var requestString =
                $"{method} {uri.PathAndQuery} HTTP/1.1\r\n" +
                $"Host: {uri.Host}:{uri.Port}\r\n" +
                $"Accept: */*\r\n" +
                $"\r\n" +
                body;

            if (!string.IsNullOrEmpty(body))
            {
                requestString = requestString.Insert(requestString.IndexOf("Accept: */*\r\n"),
                    $"Content-Length: {Encoding.ASCII.GetBytes(body).Length}\r\n");
            }

            var requestBytes = Encoding.ASCII.GetBytes(requestString);

            var responseLength = -1;

            // Calculate response length
            using (var socket = CreateSocket(uri).Result)
            {
                Console.WriteLine("Request:");
                Console.Write(requestString);
                socket.Send(requestBytes);

                var responseBuffer = new byte[1024 * 1024];
                int bytesReceived = 0;
                while (true)
                {
                    var b = socket.Receive(responseBuffer, bytesReceived, responseBuffer.Length - bytesReceived, SocketFlags.None);
                    if (b > 0)
                    {
                        bytesReceived += b;
                        if (ResponseComplete(responseBuffer, bytesReceived))
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                responseLength = bytesReceived;

                Console.WriteLine("Response:");
                Console.WriteLine(Encoding.ASCII.GetString(responseBuffer, 0, responseLength));
                Console.WriteLine();

                Console.WriteLine("Response Length:");
                Console.WriteLine(responseLength);
                Console.WriteLine();
            }

            // Adjust request and response for pipelining
            requestString = String.Concat(Enumerable.Repeat(requestString, pipeline));
            requestBytes = Encoding.ASCII.GetBytes(requestString);
            responseLength *= pipeline;

            var threadObjects = new Thread[connections];
            for (var i = 0; i < connections; i++)
            {
                var thread = new Thread(() =>
                {
                    var responseBuffer = new byte[responseLength];

                    if (keepalive)
                    {
                        using (var socket = CreateSocket(uri).Result)
                        {
                            Interlocked.Increment(ref _connections);

                            while (!cancellationToken.IsCancellationRequested)
                            {
                                SendReceive(socket, requestBytes, responseLength, responseBuffer);
                                Interlocked.Add(ref _requests, pipeline);
                            }
                        }
                    }
                    else
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            using (var socket = CreateSocket(uri).Result)
                            {
                                Interlocked.Increment(ref _connections);
                                SendReceive(socket, requestBytes, responseLength, responseBuffer);
                                Interlocked.Add(ref _requests, pipeline);
                            }
                            Interlocked.Decrement(ref _connections);
                        }
                    }
                });
                threadObjects[i] = thread;
            }

            _stopwatch.Start();

            for (var i = 0; i < connections; i++)
            {
                threadObjects[i].Start();
            }

            for (var i = 0; i < connections; i++)
            {
                threadObjects[i].Join();
            }

            writeResultsTask.Wait();

            return 0;
        }

        private static bool ResponseComplete(byte[] buffer, int count)
        {
            var response = Encoding.ASCII.GetString(buffer, 0, count);

            // Determine if response uses Content-Length or Chunked
            int? contentLength = null;
            bool chunked = false;
            using (var reader = new StringReader(response))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("HTTP") && line.Length >= 12)
                    {
                        var statusCode = int.Parse(line.Substring(9, 3));
                        if (statusCode < 200 || statusCode >= 400)
                        {
                            Console.WriteLine($"Response status code was not 2XX or 3XX");
                            Environment.Exit(1);
                        }
                    }
                    else if (line.StartsWith("Content-Length: ", StringComparison.OrdinalIgnoreCase))
                    {
                        contentLength = int.Parse(line.Substring("Content-Length: ".Length));
                        break;
                    }
                    else if (line.Equals("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
                    {
                        chunked = true;
                        break;
                    }
                }
            }

            if (contentLength != null)
            {
                var bodyDelimiter = response.IndexOf("\r\n\r\n");
                return count == (bodyDelimiter + 4 + contentLength);
            }
            else if (chunked)
            {
                // Shortcut which handles most chunked responses without parsing all the chunks
                return response.EndsWith("\r\n0\r\n\r\n");
            }
            else
            {
                return false;
            }
        }

        private static void Init(int connections, TimeSpan duration, int pipeline, bool keepalive, Uri uri)
        {
#if DEBUG
            Console.WriteLine($"Configuration: Debug");
#else
            Console.WriteLine($"Configuration: Release");
#endif

            var gc = GCSettings.IsServerGC ? "server" : "client";
            Console.WriteLine($"GC: {gc}");

            Console.WriteLine($"Connections: {connections}");
            Console.WriteLine($"Duration: {duration}");
            Console.WriteLine($"Pipeline: {pipeline}");
            Console.WriteLine($"Keepalive: {keepalive}");
            Console.WriteLine($"Uri: {uri}");

            Console.WriteLine();
        }

        private static async Task<Socket> CreateSocket(Uri uri)
        {
            var tcs = new TaskCompletionSource<Socket>();

            var socketArgs = new SocketAsyncEventArgs();
            socketArgs.RemoteEndPoint = new DnsEndPoint(uri.DnsSafeHost, uri.Port);
            socketArgs.Completed += (s, e) => tcs.TrySetResult(e.ConnectSocket);

            // Must use static ConnectAsync(), since instance Connect() does not support DNS names on OSX/Linux.
            if (Socket.ConnectAsync(SocketType.Stream, ProtocolType.Tcp, socketArgs))
            {
                await tcs.Task;
            }

            var socket = socketArgs.ConnectSocket;

            if (socket == null)
            {
                throw new SocketException((int)socketArgs.SocketError);
            }
            else
            {
                return socket;
            }
        }

        private static void SendReceive(Socket socket, byte[] requestBytes, int responseLength, byte[] responseBuffer)
        {
            socket.Send(requestBytes);

            int bytesReceived = 0;
            while (bytesReceived < responseLength)
            {
                bytesReceived += socket.Receive(responseBuffer, bytesReceived, responseBuffer.Length - bytesReceived, SocketFlags.None);
            }
        }

        private static async Task WriteResults(CancellationToken cancellationToken)
        {
            var lastRequests = (long)0;
            var lastElapsed = TimeSpan.Zero;

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));

                var currentRequests = _requests - lastRequests;
                lastRequests = _requests;

                var elapsed = _stopwatch.Elapsed;
                var currentElapsed = elapsed - lastElapsed;
                lastElapsed = elapsed;

                WriteResult(_requests, currentRequests, currentElapsed);
            }

            Console.WriteLine();
            Console.WriteLine($"Average RPS: {Math.Round(_requests / _stopwatch.Elapsed.TotalSeconds)}");
        }

        private static void WriteResult(long totalRequests, long currentRequests, TimeSpan elapsed)
        {
            Console.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] Connections: {_connections}, Requests: {totalRequests}, RPS: {Math.Round(currentRequests / elapsed.TotalSeconds)}");
        }
    }
}