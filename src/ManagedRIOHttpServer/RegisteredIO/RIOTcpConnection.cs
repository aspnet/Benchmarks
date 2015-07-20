// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace ManagedRIOHttpServer.RegisteredIO
{
    public unsafe sealed class RIOTcpConnection : IDisposable
    {
        long _connectionId;
        IntPtr _socket;
        IntPtr _requestQueue;
        RIO _rio;
        RIOWorkBundle _wb;

        long _sendCount = 0;
        long _receiveRequestCount = 0;

        RIOReceiveTask[] _receiveTasks;
        RIOPooledSegment[] _sendSegments;
        ArraySegment<byte>[] _receiveRequestBuffers;
        public const int MaxPendingReceives = 16;
        public const int MaxPendingSends = MaxPendingReceives * 2;
        const int ReceiveMask = MaxPendingReceives - 1;
        const int SendMask = MaxPendingSends - 1;

        internal RIOTcpConnection(IntPtr socket, long connectionId, RIOWorkBundle wb, RIO rio)
        {
            _socket = socket;
            _connectionId = connectionId;
            _rio = rio;
            _wb = wb;

            _requestQueue = _rio.CreateRequestQueue(_socket, MaxPendingReceives, 1, MaxPendingSends, 1, wb.completionQueue, wb.completionQueue, connectionId);
            if (_requestQueue == IntPtr.Zero)
            {
                var error = RIOImports.WSAGetLastError();
                RIOImports.WSACleanup();
                throw new Exception(String.Format("ERROR: CreateRequestQueue returned {0}", error));
            }

            _receiveTasks = new RIOReceiveTask[MaxPendingReceives];
            _receiveRequestBuffers = new ArraySegment<byte>[MaxPendingReceives];

            for (var i = 0; i < _receiveTasks.Length; i++)
            {
                _receiveTasks[i] = new RIOReceiveTask(this, wb.bufferPool.GetBuffer());
            }

            _sendSegments = new RIOPooledSegment[MaxPendingSends];
            for (var i = 0; i < _sendSegments.Length; i++)
            {
                _sendSegments[i] = wb.bufferPool.GetBuffer();
            }

            wb.connections.TryAdd(connectionId, this);

            for (var i = 0; i < _receiveTasks.Length; i++)
            {
                PostReceive(i);
            }
        }

        const RIO_SEND_FLAGS MessagePart = RIO_SEND_FLAGS.DEFER | RIO_SEND_FLAGS.DONT_NOTIFY;
        const RIO_SEND_FLAGS MessageEnd = RIO_SEND_FLAGS.NONE | RIO_SEND_FLAGS.DONT_NOTIFY;

        int _currentOffset = 0;
        public void FlushSends()
        {
            var segment = _sendSegments[_sendCount & SendMask];
            if (_currentOffset > 0)
            {
                segment.RioBuffer.Length = (uint)_currentOffset;
                if (!_rio.Send(_requestQueue, &segment.RioBuffer, 1, MessageEnd, -_sendCount - 1))
                {
                    throw new ApplicationException("Send failed");
                }
                _currentOffset = 0;
                _sendCount++;
            }
        }
        public void QueueSend(ArraySegment<byte> buffer, bool isEnd)
        {
            var segment = _sendSegments[_sendCount & SendMask];
            var count = buffer.Count;
            var offset = buffer.Offset;

            do
            {
                var length = count >= RIOBufferPool.PacketSize - _currentOffset ? RIOBufferPool.PacketSize - _currentOffset : count;
                Buffer.BlockCopy(buffer.Array, offset, segment.Buffer, segment.Offset + _currentOffset, length);
                _currentOffset += length;

                if (_currentOffset == RIOBufferPool.PacketSize)
                {
                    segment.RioBuffer.Length = RIOBufferPool.PacketSize;
                    _rio.Send(_requestQueue, &segment.RioBuffer, 1, (((_sendCount & SendMask) == 0) ? MessageEnd : MessagePart), -_sendCount - 1);
                    _currentOffset = 0;
                    _sendCount++;
                    segment = _sendSegments[_sendCount & SendMask];
                }
                else if (_currentOffset > RIOBufferPool.PacketSize)
                {
                    throw new Exception("Overflowed buffer");
                }

                offset += length;
                count -= length;
            } while (count > 0);

            if (isEnd)
            {
                if (_currentOffset > 0)
                {
                    segment.RioBuffer.Length = (uint)_currentOffset;
                    if (!_rio.Send(_requestQueue, &segment.RioBuffer, 1, MessageEnd, -_sendCount - 1))
                    {
                        throw new ApplicationException("Send failed");

                    }
                    _currentOffset = 0;
                    _sendCount++;
                }
                else
                {
                    if (!_rio.Send(_requestQueue, null, 0, RIO_SEND_FLAGS.COMMIT_ONLY, 0))
                    {
                        throw new ApplicationException("Send failed");
                    }
                    _currentOffset = 0;
                    _sendCount++;
                }
            }
        }
        public void SendCachedBad()
        {
            fixed (RIO_BUFSEGMENT* pSeg = &_wb.cachedBad)
            {
                _rio.Send(_requestQueue, pSeg, 1, MessageEnd, RIO.CachedValue);
            }
        }
        public void SendCachedBusy()
        {
            fixed (RIO_BUFSEGMENT* pSeg = &_wb.cachedBusy)
            {
                _rio.Send(_requestQueue, pSeg, 1, MessageEnd, RIO.CachedValue);
            }
        }

        public void CompleteReceive(long RequestCorrelation, uint BytesTransferred)
        {
            var receiveIndex = RequestCorrelation & ReceiveMask;
            var receiveTask = _receiveTasks[receiveIndex];
            receiveTask.Complete(BytesTransferred, (uint)receiveIndex);
        }

        internal void PostReceive(long receiveIndex)
        {
            var receiveTask = _receiveTasks[receiveIndex];
            _rio.Receive(_requestQueue, ref receiveTask._segment.RioBuffer, 1, RIO_RECEIVE_FLAGS.NONE, receiveIndex);
        }

        public RIOReceiveTask ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            var receiveIndex = (Interlocked.Increment(ref _receiveRequestCount) - 1) & ReceiveMask;
            var receiveTask = _receiveTasks[receiveIndex];
            receiveTask.SetBuffer(buffer);
            return receiveTask;
        }

        public void Close()
        {
            Dispose(true);
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //_receiveTask.Dispose();
                }

                RIOTcpConnection connection;
                _wb.connections.TryRemove(_connectionId, out connection);
                RIOImports.closesocket(_socket);
                for (var i = 0; i < _receiveTasks.Length; i++)
                {
                    _receiveTasks[i].Dispose();
                }

                for (var i = 0; i < _sendSegments.Length; i++)
                {
                    _sendSegments[i].Dispose();
                }

                disposedValue = true;
            }
        }

        ~RIOTcpConnection()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

    }

}
