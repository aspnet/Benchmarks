// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Numerics;
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
            Console.WriteLine("Starting Managed Registered IO Server");
            Console.WriteLine("* Hardware Accelerated SIMD: {0}", Vector.IsHardwareAccelerated);



            unsafe
            {
                if (sizeof(IntPtr) != 8)
                {
                    Console.WriteLine("ManagedRIOHttpServer needs to be run in x64 mode");
                    return;
                }
            }

            try
            {
                // TODO: Use safehandles everywhere!
                var ss = new RIOTcpServer(5000, 0, 0, 0, 0);

                ThreadPool.SetMinThreads(100, 100);

                while (true)
                {
                    var socket = ss.Accept();
                    ThreadPool.UnsafeQueueUserWorkItem(Serve, socket);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Start up issue {0}", ex.Message);
            }
        }


        static void Serve(object state)
        {
            var socket = (RIOTcpConnection)state;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            ServeSocket(socket);
#pragma warning restore CS4014
        }

        // 17 delimiter bytes to allow offset of 1 start
        static byte[] delimiterBytes = new byte[] {
            0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd
        };
        static byte[] careBytes = new byte[] {
            0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0,
            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff
        };

        static async Task ServeSocket(RIOTcpConnection socket)
        {
            try
            {
                var headerBuffer = new ArraySegment<byte>(_headersBytes, 0, _headersBytes.Length);
                var bodyBuffer = new ArraySegment<byte>(_bodyBytes, 0, _bodyBytes.Length);
                var buffer0 = new byte[8192 + 64]; // max header size + cache line buffer
                var buffer1 = new byte[8192 + 64]; // max header size + cache line buffer
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
                    if (overflow > 0)
                    {
                        unsafe
                        {
                            fixed (byte* b = buffer)
                            {
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
                            }
                        }
                        overflow = 0;
                    }

                    var last = start;

                    var delimStart = new Vector<byte>(0xd); // '\r'
                    var delimNext = new Vector<byte>(0xa); // '\n'
                    var vTrue = new Vector<byte>(careBytes, 16);
                    var alignedDelim = Vector.AsVectorInt32(new Vector<byte>(delimiterBytes, 0));

                    var ul = r - 3;
                    var hasStart = false;
                    

                    for (var i = start; i < buffer.Length - Vector<byte>.Count; i += Vector<byte>.Count)
                    {
                        if (i > r)
                        {
                            break;
                        }
                        // buffer is more than 15 bytes larger than read for safety
                        var v0 = new Vector<byte>(buffer, i);
                        var v1 = new Vector<byte>(buffer, i + 1);

                        hasStart = Vector.EqualsAny(v0, delimStart);
                        var hasSecond = Vector.EqualsAny(v1, delimNext);
                        if (hasStart)
                        {
                            if (hasSecond)
                            {
                                var v2 = new Vector<byte>(buffer, i + 2);
                                var v3 = new Vector<byte>(buffer, i + 3);
                                if (Vector.EqualsAny(alignedDelim, Vector.AsVectorInt32(v0)))
                                {
                                    count++;
                                    last = i + 17; // cheat, can't be another header terminator within 16 bytes
                                    continue;
                                }
                                if (Vector.EqualsAny(alignedDelim, Vector.AsVectorInt32(v1)))
                                {
                                    count++;
                                    last = i + 18; // cheat, can't be another header terminator within 16 bytes
                                    continue;
                                }
                                if (Vector.EqualsAny(alignedDelim, Vector.AsVectorInt32(v2)))
                                {
                                    count++;
                                    last = i + 19; // cheat, can't be another header terminator within 16 bytes
                                    continue;
                                }
                                if (Vector.EqualsAny(alignedDelim, Vector.AsVectorInt32(v3)))
                                {
                                    count++;
                                    last = i + 20; // cheat, can't be another header terminator within 16 bytes
                                    continue;
                                }
                            }
                        }
                    }

                    if (hasStart && last < r)
                    {
                        unsafe
                        {
                            fixed (byte* b = buffer)
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
                        }
                    }
                    else
                    {
                        overflow = 0;
                    }

                    if (count == 0)
                    {
                        socket.SendCachedBad();
                        break;
                    }

                    var date = DateTime.UtcNow.ToString("r");
                    Encoding.UTF8.GetBytes(date, 0, dateBytes.Length, dateBytes, 0);

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


        public static void LowerCaseSIMD(byte[] data)
        {
            var A = new Vector<byte>(65); // A
            var Z = new Vector<byte>(90); // Z

            var ul = data.Length - 16;
            for (var o = 0; o < ul; o += 16)
            {
                var v = new Vector<byte>(data, o);

                v = Vector.ConditionalSelect(
                    Vector.BitwiseAnd(
                        Vector.GreaterThanOrEqual(v, A),
                        Vector.LessThanOrEqual(v, Z)
                    ),
                    Vector.BitwiseOr(new Vector<byte>(0x20), v), // 0010 0000
                    v
                );
                v.CopyTo(data, o);
            }
        }
    }

}

