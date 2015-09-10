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
    sealed class RIOServer
    {
        #region "Body"
        private const string bodyStr = "Hello, World!";
        private static byte[] _bodyBytesSource = Encoding.UTF8.GetBytes(bodyStr);

        private ThreadLocal<byte[]> _threadBody = new ThreadLocal<byte[]>(()=> {
            var bytes = new byte[_bodyBytesSource.Length];
            Buffer.BlockCopy(_bodyBytesSource, 0, bytes, 0, bytes.Length);
            return bytes;
        }, true);
        #endregion

        #region "Headers"
        private const string headersKeepAliveStr = "HTTP/1.1 200 OK\r\n" +
            "Server:-RIO-\r\n" +
            "Content-Type:text/plain\r\n" +
            "Content-Length:13\r\n" +
            "Date:DDD, dd mmm yyyy hh:mm:ss GMT" +
            "\r\n\r\n";
        private static readonly byte[] _headerBytesSource = Encoding.ASCII.GetBytes(headersKeepAliveStr);

        private Timer _updateDateTimer;

        private static byte[] InitaliseHeader()
        {
            var bytes = new byte[_headerBytesSource.Length];
            Buffer.BlockCopy(_headerBytesSource, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private int headerIndex = 0;
        private ThreadLocal<byte[]> _threadHeader0 = new ThreadLocal<byte[]>(InitaliseHeader, true);
        private ThreadLocal<byte[]> _threadHeader1 = new ThreadLocal<byte[]>(InitaliseHeader, true);
        private void SetupHeaderUpdate()
        {
            var start = _headerBytesSource.Length - 33;
            _updateDateTimer = new Timer((obj) =>
            {
                var date = DateTime.UtcNow.ToString("r");
                Encoding.ASCII.GetBytes(date, 0, date.Length, _headerBytesSource, start);

                var newIndex = (headerIndex + 1) & 1;
                var headers = newIndex == 0 ? _threadHeader0 : _threadHeader1;

                foreach (var header in headers.Values)
                {
                    Buffer.BlockCopy(_headerBytesSource, start, header, start, date.Length);
                }

                headerIndex = newIndex;
            }, null, 0, 1000);
        }
        #endregion

        public RIOServer(ushort port)
        {
            SetupHeaderUpdate();
            Port = port;
        }

        public ushort Port { get; }

        public void Start()
        {
            var ss = new RIOTcpServer(Port, 0, 0, 0, 0);
            Console.WriteLine("* Listening on port: {0}", Port);
            
            while (true)
            {
                var socket = ss.Accept();
                ThreadPool.UnsafeQueueUserWorkItem(Serve, socket);
            }
        }

        public Task StartAsync(CancellationToken token)
        {
            return Task.Run(() =>
            {
                var ss = new RIOTcpServer(Port, 0, 0, 0, 0);
                Console.WriteLine("* Listening on port: {0}", Port);
                
                while (!token.IsCancellationRequested)
                {
                    var socket = ss.Accept();
                    ThreadPool.UnsafeQueueUserWorkItem(Serve, socket);
                }
            });
        }
        
        private void Serve(object state)
        {
            var socket = (RIOTcpConnection)state;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            ServeSocket(socket);
#pragma warning restore CS4014
        }

        // 65 delimiter bytes to allow offset of 1 start
        static readonly byte[] delimiterBytes = new byte[] {
            0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, // SSE 128
            0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, // AVX 256
            0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa,
            0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, 0xd, 0xa, // AVX2 512
            0xd
        };
        private async Task ServeSocket(RIOTcpConnection socket)
        {
            try
            {
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
                    var delimVector = new Vector<byte>(delimiterBytes, 0);

                    var alignedDelim = Vector.AsVectorInt32(delimVector);

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

                    var headerBytes = headerIndex == 0 ? _threadHeader0.Value : _threadHeader1.Value;
                    var bodyBytes = _threadBody.Value;

                    var headerBuffer = new ArraySegment<byte>(headerBytes, 0, headerBytes.Length);
                    var bodyBuffer = new ArraySegment<byte>(bodyBytes, 0, bodyBytes.Length);

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
    }
}
