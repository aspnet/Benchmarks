// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;

namespace ManagedRIOHttpServer.RegisteredIO
{
    internal class RIOThreadPool
    {
        private RIO _rio;
        private CancellationToken _token;
        private int _maxThreads;

        public const int MaxOpenSocketsPerThread = 1024;
        private const int MaxOutsandingCompletions = (RIOTcpConnection.MaxPendingReceives + RIOTcpConnection.MaxPendingSends) * MaxOpenSocketsPerThread;

        private IntPtr _socket;

        internal RIOWorkBundle GetWorker(long connetionId)
        {
            return _workers[(connetionId % _maxThreads)];
        }

        private RIOWorkBundle[] _workers;

        public unsafe RIOThreadPool(RIO rio, IntPtr socket, CancellationToken token)
        {
            _socket = socket;
            _rio = rio;
            _token = token;

            _maxThreads = Environment.ProcessorCount;

            _workers = new RIOWorkBundle[_maxThreads];
            for (var i = 0; i < _workers.Length; i++)
            {
                var worker = new RIOWorkBundle()
                {
                    id = i,
                    bufferPool = new RIOBufferPool(_rio)
                };
                worker.completionPort = CreateIoCompletionPort(INVALID_HANDLE_VALUE, IntPtr.Zero, 0, 0);

                if (worker.completionPort == IntPtr.Zero)
                {
                    var error = GetLastError();
                    RIOImports.WSACleanup();
                    throw new Exception(string.Format("ERROR: CreateIoCompletionPort returned {0}", error));
                }

                var completionMethod = new RIO_NOTIFICATION_COMPLETION()
                {
                    Type = RIO_NOTIFICATION_COMPLETION_TYPE.IOCP_COMPLETION,
                    Iocp = new RIO_NOTIFICATION_COMPLETION_IOCP()
                    {
                        IocpHandle = worker.completionPort,
                        QueueCorrelation = (ulong)i,
                        Overlapped = (NativeOverlapped*)(-1)// nativeOverlapped
                    }
                };
                worker.completionQueue = _rio.CreateCompletionQueue(MaxOutsandingCompletions, completionMethod);

                if (worker.completionQueue == IntPtr.Zero)
                {
                    var error = RIOImports.WSAGetLastError();
                    RIOImports.WSACleanup();
                    throw new Exception(String.Format("ERROR: CreateCompletionQueue returned {0}", error));
                }

                worker.connections = new ConcurrentDictionary<long, RIOTcpConnection>();
                worker.thread = new Thread(GetThreadStart(i));
                worker.thread.Name = "RIOThread " + i.ToString();
                worker.thread.IsBackground = true;
                _workers[i] = worker;
            }

            // gc
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);

            //GC.Collect(2, GCCollectionMode.Forced, true);
            //GC.WaitForPendingFinalizers();
            //GC.Collect(2, GCCollectionMode.Forced, true);

            for (var i = 0; i < _workers.Length; i++)
            {
                // pin buffers
                _workers[i].bufferPool.Initalize();
            }


            for (var i = 0; i < _workers.Length; i++)
            {
                _workers[i].thread.Start();
            }
        }
        private ThreadStart GetThreadStart(int i)
        {
            return new ThreadStart(() =>
            {
                Process(i);
            });

        }

        static readonly string badResponseStr = "HTTP/1.1 400 Bad Request\r\n" +
            "Content-Type: text/plain;charset=UTF-8\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: keep-alive\r\n" +
            "Server: -RIO-\r\n" +
            "\r\n";

        private static byte[] _badResponseBytes = Encoding.UTF8.GetBytes(badResponseStr);

        static readonly string busyResponseStr = "HTTP/1.1 503 Service Unavailable\r\n" +
            "Content-Type: text/plain;charset=UTF-8\r\n" +
            "Content-Length: 4\r\n" +
            "Connection: keep-alive\r\n" +
            "Server: -RIO-\r\n" +
            "\r\n" +
            "Busy";

        private static byte[] _busyResponseBytes = Encoding.UTF8.GetBytes(busyResponseStr);

        const int maxResults = 512;
        private unsafe void Process(int id)
        {
            RIO_RESULT* results = stackalloc RIO_RESULT[maxResults];
            uint bytes, key;
            NativeOverlapped* overlapped;

            var worker = _workers[id];
            var completionPort = worker.completionPort;
            var cq = worker.completionQueue;

            RIOPooledSegment cachedBadBuffer = worker.bufferPool.GetBuffer();
            Buffer.BlockCopy(_badResponseBytes, 0, cachedBadBuffer.Buffer, cachedBadBuffer.Offset, _badResponseBytes.Length);
            cachedBadBuffer.RioBuffer.Length = (uint)_badResponseBytes.Length;
            worker.cachedBad = cachedBadBuffer.RioBuffer;

            RIOPooledSegment cachedBusyBuffer = worker.bufferPool.GetBuffer();
            Buffer.BlockCopy(_busyResponseBytes, 0, cachedBusyBuffer.Buffer, cachedBusyBuffer.Offset, _busyResponseBytes.Length);
            cachedBusyBuffer.RioBuffer.Length = (uint)_busyResponseBytes.Length;
            worker.cachedBusy = cachedBusyBuffer.RioBuffer;

            uint count;
            int ret;
            RIO_RESULT result;
            while (!_token.IsCancellationRequested)
            {
                _rio.Notify(cq);
                var sucess = GetQueuedCompletionStatus(completionPort, out bytes, out key, out overlapped, -1);
                if (sucess)
                {
                    count = _rio.DequeueCompletion(cq, (IntPtr)results, maxResults);
                    ret = _rio.Notify(cq);
                    for (var i = 0; i < count; i++)
                    {
                        result = results[i];
                        if (result.RequestCorrelation >= 0)
                        {
                            // receive
                            RIOTcpConnection connection;
                            if (worker.connections.TryGetValue(result.ConnectionCorrelation, out connection))
                            {
                                connection.CompleteReceive(result.RequestCorrelation, result.BytesTransferred);
                            }
                        }
                    }
                }
                else
                {
                    var error = GetLastError();
                    if (error != 258)
                    {
                        throw new Exception(string.Format("ERROR: GetQueuedCompletionStatusEx returned {0}", error));
                    }
                }
            }
            cachedBadBuffer.Dispose();
            cachedBusyBuffer.Dispose();
        }

        const string Kernel_32 = "Kernel32";
        const long INVALID_HANDLE_VALUE = -1;

        [DllImport(Kernel_32, SetLastError = true), SuppressUnmanagedCodeSecurity]
        private unsafe static extern IntPtr CreateIoCompletionPort(long handle, IntPtr hExistingCompletionPort, int puiCompletionKey, uint uiNumberOfConcurrentThreads);

        [DllImport(Kernel_32, SetLastError = true), SuppressUnmanagedCodeSecurity]
        private static extern unsafe bool GetQueuedCompletionStatus(IntPtr CompletionPort, out uint lpNumberOfBytes, out uint lpCompletionKey, out NativeOverlapped* lpOverlapped, int dwMilliseconds);

        [DllImport(Kernel_32, SetLastError = true), SuppressUnmanagedCodeSecurity]
        private static extern long GetLastError();

    }
}
