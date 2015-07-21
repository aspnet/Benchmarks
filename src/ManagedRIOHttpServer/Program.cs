// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedRIOHttpServer.RegisteredIO;

namespace ManagedRIOHttpServer
{
    public sealed class Program
    {
        static readonly string headersKeepAliveStr = "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/plain\r\n" +
            "Content-Length:13\r\n" +
            "Connection:keep-alive\r\n" +
            "Server:-RIO-\r\n" +
            "Date:";

        private static byte[] _headersBytes = Encoding.UTF8.GetBytes(headersKeepAliveStr);
        
        static readonly string bodyStr = "\r\n\r\n" +
            "Hello, World!";

        private static byte[] _bodyBytes = Encoding.UTF8.GetBytes(bodyStr);
            

        static void Main(string[] args)
        {
            unsafe
            {
                if (sizeof(IntPtr) != 8)
                {
                    Console.WriteLine("ManagedRIOHttpServer needs to be run in x64 mode");
                    return;
                }
            }

            // TODO: Use safehandles everywhere!
            var ss = new RIOTcpServer(5000, 0, 0, 0, 0);

            ThreadPool.SetMinThreads(100, 100);

            while (true)
            {
                var socket = ss.Accept();
                ThreadPool.UnsafeQueueUserWorkItem(Serve, socket);
            }
        }


        static void Serve(object state)
        {
            var socket = (RIOTcpConnection)state;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            ServeSocket(socket);
#pragma warning restore CS4014
        }

        static async Task ServeSocket(RIOTcpConnection socket)
        {
            try
            {
                var headerBuffer = new ArraySegment<byte>(_headersBytes, 0, _headersBytes.Length);
                var bodyBuffer = new ArraySegment<byte>(_bodyBytes, 0, _bodyBytes.Length);
                var buffer0 = new byte[2048];
                var buffer1 = new byte[2048];
                var receiveBuffer0 = new ArraySegment<byte>(buffer0, 0, buffer0.Length);
                var receiveBuffer1 = new ArraySegment<byte>(buffer1, 0, buffer1.Length);

                var receiveTask = socket.ReceiveAsync(receiveBuffer0, CancellationToken.None);

                var dateBytes = Encoding.UTF8.GetBytes("DDD, dd mmm yyyy hh:mm:ss GMT");

                var loop = 0;
                var overflow = 0;
                // need to check for keep alive

                while (true)
                {
                    int r = (int)await receiveTask;
                    receiveTask = socket.ReceiveAsync((loop & 1) == 1 ? receiveBuffer0 : receiveBuffer1, CancellationToken.None);

                    if (r == 0)
                    {
                        if (loop > 0)
                        {
                            socket.FlushSends();
                        }
                        break;
                    }

                    var buffer = (loop & 1) == 0 ? buffer0 : buffer1;

                    // need to handle packet splits

                    var count = 0;
                    var start = 0;
                    
                    // pipelining check
                    unsafe
                    {

                        fixed (byte* b = buffer)
                        {
                            if (overflow > 0)
                            {
                                // TODO: some more corner cases + better handling
                                switch (overflow)
                                {
                                    case 1:
                                        if (b[0] == 0xa && b[1] == 0xd && b[2] == 0xa)
                                        {
                                            count++;
                                            start = 3;
                                        }
                                        break;
                                    case 2:
                                        if (b[0] == 0xd && b[1] == 0xa)
                                        {
                                            count++;
                                            start = 2;
                                        }
                                        break;
                                    case 3:
                                        if (b[0] == 0xa)
                                        {
                                            count++;
                                            start = 1;
                                        }
                                        break;
                                }
                                overflow = 0;
                            }

                            var last = start;

                            // need to read 4 bytes to match so end loop 3 bytes earlier than count
                            var ul = r - 3;
                            for (var i = start; i < ul; i++)
                            {
                                if (b[i] == 0xd && b[i + 1] == 0xa && b[i + 2] == 0xd && b[i + 3] == 0xa )
                                {
                                    count++;
                                    i += 3;
                                    last = i + 1;
                                }
                            }
                            // TODO: some more corner cases + better handling
                            if (last < r)
                            {
                                // doesn't end with terminator
                                switch (r - last)
                                {
                                    case 1:
                                        if (b[r - 1] == 0xd)
                                        {
                                            overflow++;
                                        }
                                        break;
                                    case 2:
                                        if (b[r - 2] == 0xd && b[r - 1] == 0xa)
                                        {
                                            overflow += 2;
                                            break;
                                        }
                                        goto case 1;
                                    case 3:
                                    default:
                                        if (b[r - 3] == 0xd && b[r - 2] == 0xa && b[r - 1] == 0xd)
                                        {
                                            overflow += 3;
                                            break;
                                        }
                                        goto case 2;
                                }
                            }
                            else
                            {
                                overflow = 0;
                            }
                        }
                    }

                    if (count == 0)
                    {
                        socket.SendCachedBad();
                        break;
                    }

                    var date = DateTime.UtcNow.ToString("r");
                    Encoding.UTF8.GetBytes(date,0, dateBytes.Length, dateBytes, 0);

                    for (var i = 1; i < count; i++)
                    {
                        socket.QueueSend(headerBuffer, false);
                        socket.QueueSend(new ArraySegment<byte>(dateBytes), false);
                        socket.QueueSend(bodyBuffer, false);
                    }
                    socket.QueueSend(headerBuffer, false);
                    socket.QueueSend(new ArraySegment<byte>(dateBytes), false);
                    // force send if not more ready to recieve/pack
                    var nextReady = receiveTask.IsCompleted;
                    socket.QueueSend(bodyBuffer, (!nextReady));
                    
                    loop++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                socket.Close();
            }
        }
    }
    
    }

