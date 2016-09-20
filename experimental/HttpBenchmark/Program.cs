// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Extensions.CommandLineUtils;
using System;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Linq;
using System.Threading;
using System.Runtime;
using System.Threading.Tasks;

namespace HttpBenchmark
{
    public class Program
    {
        private static Stopwatch _stopwatch = new Stopwatch();
        private static long _requests;
        private static long _connections;

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                Name = "HttpBenchmark",
                FullName = "HTTP Benchmark",
                Description = "HTTP benchmarking tool"
            };

            app.HelpOption("-?|-h|--help");

            var connectionsOption = app.Option("-c|--connections", "Number of connections", CommandOptionType.SingleValue);
            var durationOption = app.Option("-d|--duration", "Duration of test in seconds", CommandOptionType.SingleValue);
            var pipelineOption = app.Option("-p|--pipeline",
                "Number of HTTP requests to pipeline in a single network roundtrip.  Default is 1 (no pipelining).",
                CommandOptionType.SingleValue);
            var keepaliveOption = app.Option("-k|--keepalive", "Reuse connections for multiple requests.  Default is TRUE.",
                CommandOptionType.SingleValue);

            var urlArgument = app.Argument("url", "URL to benchmark");

            app.OnExecute(() =>
            {
                var connectionsValue = connectionsOption.Value();
                if (string.IsNullOrEmpty(connectionsValue))
                {
                    connectionsValue = "1";
                }

                var durationValue = durationOption.Value();
                if (string.IsNullOrEmpty(durationValue))
                {
                    durationValue = "10";
                }

                var pipelineValue = pipelineOption.Value();
                if (string.IsNullOrEmpty(pipelineValue))
                {
                    pipelineValue = "1";
                }

                var keepaliveValue = keepaliveOption.Value();
                if (string.IsNullOrEmpty(keepaliveValue))
                {
                    keepaliveValue = "true";
                }

                var url = urlArgument.Value;

                int connections;
                int duration;
                int pipeline;
                bool keepalive;
                Uri uri;

                if (!int.TryParse(connectionsValue, out connections) ||
                    !int.TryParse(durationValue, out duration) ||
                    !int.TryParse(pipelineValue, out pipeline) ||
                    !bool.TryParse(keepaliveValue, out keepalive) ||
                    !Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                    connections <= 0 ||
                    duration <= 0 ||
                    pipeline <= 0)
                {
                    app.ShowHelp();
                    return 2;
                }

                return Run(connections, TimeSpan.FromSeconds(duration), pipeline, keepalive, uri);
            });

            return app.Execute(args);
        }

        private static int Run(int connections, TimeSpan duration, int pipeline, bool keepalive, Uri uri)
        {
            Init(duration);

            var requestString =
                $"GET {uri.PathAndQuery} HTTP/1.1\r\n" +
                $"Host: {uri.Host}:{uri.Port}\r\n" +
                $"Accept: */*\r\n" +
                $"\r\n";

            var requestBytes = Encoding.ASCII.GetBytes(requestString);

            var responseLength = -1;

            // Calculate response length
            using (var socket = CreateSocket(uri).Result)
            {
                var requestStringWithConnectionClose = requestString.Replace("Host:", "Connection: Close\r\nHost:");
                socket.Send(Encoding.ASCII.GetBytes(requestStringWithConnectionClose));

                var responseBuffer = new byte[1024];
                int bytesReceived = 0;
                while (true)
                {
                    var b = socket.Receive(responseBuffer, bytesReceived, responseBuffer.Length - bytesReceived, SocketFlags.None);
                    if (b > 0)
                    {
                        bytesReceived += b;
                    }
                    else
                    {
                        break;
                    }
                }

                responseLength = bytesReceived - "Connection: close\r\n".Length;

                Console.WriteLine($"Response Length: {responseLength}");

                Console.WriteLine($"Response:");
                Console.WriteLine(Encoding.ASCII.GetString(responseBuffer, 0, bytesReceived)
                    .Replace("Connection: close\r\n", String.Empty));
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

                            while (true)
                            {
                                SendReceive(socket, requestBytes, responseLength, responseBuffer);
                                Interlocked.Add(ref _requests, pipeline);
                            }
                        }
                    }
                    else
                    {
                        while (true)
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

            return 0;
        }

        private static void Init(TimeSpan duration)
        {
#if DEBUG
            Console.WriteLine($"Configuration: Debug");
#else
            Console.WriteLine($"Configuration: Release");
#endif

            var gc = GCSettings.IsServerGC ? "server" : "client";
            Console.WriteLine($"GC: {gc}");

            Console.WriteLine();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            WriteResults(duration);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
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

        private static async Task WriteResults(TimeSpan duration)
        {
            var lastRequests = (long)0;
            var lastElapsed = TimeSpan.Zero;

            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));

                var currentRequests = _requests - lastRequests;
                lastRequests = _requests;

                var elapsed = _stopwatch.Elapsed;
                var currentElapsed = elapsed - lastElapsed;
                lastElapsed = elapsed;

                WriteResult(_requests, currentRequests, currentElapsed);

                if (_stopwatch.Elapsed > duration)
                {
                    Environment.Exit(0);
                }
            }
        }

        private static void WriteResult(long totalRequests, long currentRequests, TimeSpan elapsed)
        {
            Console.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] Connections: {_connections}, Requests: {totalRequests}, RPS: {Math.Round(currentRequests / elapsed.TotalSeconds)}");
        }
    }
}