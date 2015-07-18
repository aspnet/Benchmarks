// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedRIOHttpServer.RegisteredIO;

namespace ManagedRIOHttpServer
{
    public sealed class Program
    {
        static readonly string responseKeepAliveStr = "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/plain;charset=UTF-8\r\n" +
            "Content-Length: 10\r\n" +
            "Connection: keep-alive\r\n" +
            "Server: -RIO-\r\n" +
            "\r\n" +
            "HelloWorld";

        private static byte[] _responseBytes = Encoding.UTF8.GetBytes(responseKeepAliveStr);

        static readonly string responseCloseStr = "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/plain;charset=UTF-8\r\n" +
            "Content-Length: 10\r\n" +
            "Connection: close\r\n" +
            "Server: -RIO-\r\n" +
            "\r\n" +
            "HelloWorld";

        private static byte[] _responseCloseBytes = Encoding.UTF8.GetBytes(responseCloseStr);

        static readonly string connectionStr = "connection:";
        private static byte[] _connectionBytes = Encoding.UTF8.GetBytes(connectionStr);
        static readonly string keepAliveStr = "keep-alive";
        private static byte[] _keepAliveBytes = Encoding.UTF8.GetBytes(keepAliveStr);

        static readonly string dataDivisionStr = "\r\n\r\n";
        private static byte[] _dataDivisionBytes = Encoding.UTF8.GetBytes(dataDivisionStr);

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
            var ss = new RIOTcpServer(5000, 127, 0, 0, 1);

            ThreadPool.SetMinThreads(100, 100);

            while (true)
            {
                var socket = ss.Accept();
                ThreadPool.UnsafeQueueUserWorkItem(Serve, socket);
            }
        }


        static void Serve(object state)
        {
            var socket = (TcpConnection)state;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            ServeSocket(socket);
#pragma warning restore CS4014
        }

        static async Task ServeSocket(TcpConnection socket)
        {
            try
            {
                var sendBuffer = new ArraySegment<byte>(_responseBytes, 0, _responseBytes.Length);
                var buffer0 = new byte[2048];
                var buffer1 = new byte[2048];
                var receiveBuffer0 = new ArraySegment<byte>(buffer0, 0, buffer0.Length);
                var receiveBuffer1 = new ArraySegment<byte>(buffer1, 0, buffer1.Length);

                var receiveTask = socket.ReceiveAsync(receiveBuffer0, CancellationToken.None);

                var loop = 0;

                while (true)
                {
                    int r = (int)await receiveTask;
                    receiveTask = socket.ReceiveAsync((loop & 1) == 1 ? receiveBuffer0 : receiveBuffer1, CancellationToken.None);

                    if (r == 0)
                    {
                        break;
                    }

                    var buffer = (loop & 1) == 0 ? buffer0 : buffer1;

                    // need to handle packet splits

                    var keepAlive = false;
                    var count = 0;
                    unsafe
                    {
                        fixed (byte* inputBuffer = buffer)
                        fixed (byte* dataDivisionBuffer = _dataDivisionBytes)
                        fixed (byte* connectionBuffer = _connectionBytes)
                        fixed (byte* keepAliveBuffer = _keepAliveBytes)
                        {
                            byte* next;
                            byte* start = inputBuffer;
                            while ((next = memchr(start, '\r', r)) != (byte*)0)
                            {
                                r = r - (int)(next - start);
                                if (r < 4)
                                {
                                    break;
                                }
                                if (_memicmp(next, dataDivisionBuffer, 4) == 0)
                                {
                                    count++;
                                    start = next + 4;
                                }
                                else
                                {
                                    start = next + 1;
                                }
                                if (!keepAlive && r > 23)
                                {
                                    if (_memicmp(next + 2, connectionBuffer, 11) == 0)
                                    {
                                        next += 13;
                                        r -= 13;
                                        while (*next == ' ' && r > 11)
                                        {
                                            next++;
                                            r--;
                                        }
                                        if (_memicmp(next, keepAliveBuffer, 10) == 0)
                                        {
                                            keepAlive = true;
                                            start = next + 10;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (count == 0)
                    {
                        socket.SendCachedBad();
                        break;
                    }

                    for (var i = 1; i < count; i++)
                    {
                        socket.QueueSend(sendBuffer, false);
                    }
                    // force send if not more ready to recieve/pack
                    socket.QueueSend(sendBuffer, (!receiveTask.IsCompleted) || (!keepAlive));

                    if (!keepAlive)
                    {
                        break;
                    }

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

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl), SuppressUnmanagedCodeSecurity]
        static unsafe extern int _memicmp(byte* b1, byte* b2, long count);
        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl), SuppressUnmanagedCodeSecurity]
        static unsafe extern byte* memchr(byte* b1, int c, long count);
    }
    
    }

