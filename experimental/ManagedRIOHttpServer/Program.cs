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
        // Number of 100ns ticks per time unit
        private const long TicksPerMillisecond = 10000;
        private const long TicksPerSecond = TicksPerMillisecond * 1000;

        static readonly string headersKeepAliveStr = "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/plain\r\n" +
            "Content-Length:13\r\n" +
            "Connection:keep-alive\r\n" +
            "Server:-RIO-\r\n" +
            "Date:DDD, dd mmm yyyy hh:mm:ss GMT" +
            "\r\n\r\n";

        private static byte[][] _headersBytesBuffers = new byte[][] {
            Encoding.ASCII.GetBytes(headersKeepAliveStr),
            Encoding.ASCII.GetBytes(headersKeepAliveStr)
        };
        private static byte[] _headersBytes;

        private static readonly string bodyStr = "Hello, World!";

        private static byte[] _bodyBytes = Encoding.UTF8.GetBytes(bodyStr);
        private static Timer UpdateDateTimer;

        static Program()
        {
            var start = _headersBytesBuffers[0].Length - 33;
            var loop = 0u;
            UpdateDateTimer = new Timer((obj) =>
            {
                var date = DateTime.UtcNow.ToString("r");
                var index = (++loop) & 1;
                Encoding.ASCII.GetBytes(date, 0, date.Length, _headersBytesBuffers[index], start);
                _headersBytes = _headersBytesBuffers[index];
            }, null, 0, 1000);
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Starting Managed Registered IO Server");
            Console.WriteLine("* Hardware Accelerated SIMD: {0}", Vector.IsHardwareAccelerated);
            Console.WriteLine("* Vector<byte>.Count: {0}", Vector<byte>.Count);

            if (IntPtr.Size != 8)
            {
                Console.WriteLine("ManagedRIOHttpServer needs to be run in x64 mode");
                return;
            }

            try
            {
                // TODO: Use safehandles everywhere!
                ushort port = 5000;
                var ss = new RIOTcpServer(port, 0, 0, 0, 0);
                Console.WriteLine("* Listening on port: {0}", port);

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
            0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, // SSE 128
            0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, // AVX 256
            0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa,
            0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, // AVX2 512
            0xd
        };
        static byte[] careBytes = new byte[] {
            0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0,
            0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0,
            0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0,
            0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0,
            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff
        };

        static async Task ServeSocket(RIOTcpConnection socket)
        {
            try
            {
                var headerBuffer = new ArraySegment<byte>(_headersBytes, 0, _headersBytes.Length);
                var bodyBuffer = new ArraySegment<byte>(_bodyBytes, 0, _bodyBytes.Length);
                var buffer0 = new byte[8192 + 64 + 64]; // max header size + AVX2 + cache line buffer
                var buffer1 = new byte[8192 + 64 + 64]; // max header size + AVX2 + cache line buffer
                var receiveBuffer0 = new ArraySegment<byte>(buffer0, 0, buffer0.Length);
                var receiveBuffer1 = new ArraySegment<byte>(buffer1, 0, buffer1.Length);

                var receiveTask = socket.ReceiveAsync(receiveBuffer0, CancellationToken.None);


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
                    //var vTrue = new Vector<byte>(careBytes, 64);
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
                                // contains header line terminator

                                var v2 = new Vector<byte>(buffer, i + 2);
                                var v3 = new Vector<byte>(buffer, i + 3);
                                if (Vector.EqualsAny(alignedDelim, Vector.AsVectorInt32(v0)))
                                {
                                    // contains headers terminator offset 0 bytes from Int32 start
                                    count++;
                                    last = i + Vector<byte>.Count + 1; // cheat, can't be another header terminator within 16 bytes
                                    continue;
                                }
                                if (Vector.EqualsAny(alignedDelim, Vector.AsVectorInt32(v1)))
                                {
                                    // contains headers terminator offset 1 bytes from Int32 start
                                    count++;
                                    last = i + Vector<byte>.Count + 2; // cheat, can't be another header terminator within 16 bytes
                                    continue;
                                }
                                if (Vector.EqualsAny(alignedDelim, Vector.AsVectorInt32(v2)))
                                {
                                    // contains headers terminator offset 2 bytes from Int32 start
                                    count++;
                                    last = i + Vector<byte>.Count + 3; // cheat, can't be another header terminator within 16 bytes
                                    continue;
                                }
                                if (Vector.EqualsAny(alignedDelim, Vector.AsVectorInt32(v3)))
                                {
                                    // contains headers terminator offset 3 bytes from Int32 start
                                    count++;
                                    last = i + Vector<byte>.Count + 2; // cheat, can't be another header terminator within 16 bytes
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


                    for (var i = 1; i < count; i++)
                    {
                        socket.QueueSend(headerBuffer, false);
                        socket.QueueSend(bodyBuffer, false);
                    }
                    socket.QueueSend(headerBuffer, false);
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

        //public static void LowerCaseSIMD(ArraySegment<byte> data)
        //{
        //    if (data.Offset + data.Count + Vector<byte>.Count < data.Array.Length)
        //    {
        //        throw new ArgumentOutOfRangeException("Nope");
        //    }
        //    var A = new Vector<byte>(65); // A
        //    var Z = new Vector<byte>(90); // Z

        //    for (var o = data.Offset; o < data.Count - Vector<byte>.Count; o += Vector<byte>.Count)
        //    {
        //        var v = new Vector<byte>(data.Array, o);

        //        v = Vector.ConditionalSelect(
        //            Vector.BitwiseAnd(
        //                Vector.GreaterThanOrEqual(v, A),
        //                Vector.LessThanOrEqual(v, Z)
        //            ),
        //            Vector.BitwiseOr(new Vector<byte>(0x20), v), // 0010 0000
        //            v
        //        );
        //        v.CopyTo(data.Array, o);
        //    }
        //}
    }

}

