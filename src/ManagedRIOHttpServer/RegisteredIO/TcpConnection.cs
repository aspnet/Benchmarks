using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ManagedRIOHttpServer.RegisteredIO
{
    public unsafe class TcpConnection : IDisposable
    {
        long _connectionId;
        IntPtr _socket;
        IntPtr _requestQueue;
        RIO _rio;
        WorkBundle _wb;

        long _sendCount = 0;
        long _receiveRequestCount = 0;

        ReceiveTask[] _receiveTasks;
        PooledSegment[] _sendSegments;
        ArraySegment<byte>[] _receiveRequestBuffers;
        public const int MaxPendingReceives = 32;
        public const int MaxPendingSends = MaxPendingReceives * 2;
        const int ReceiveMask = MaxPendingReceives - 1;
        const int SendMask = MaxPendingSends - 1;

        internal TcpConnection(IntPtr socket, long connectionId, WorkBundle wb, RIO rio)
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

            _receiveTasks = new ReceiveTask[MaxPendingReceives];
            _receiveRequestBuffers = new ArraySegment<byte>[MaxPendingReceives];

            for (var i = 0; i < _receiveTasks.Length; i++)
            {
                _receiveTasks[i] = new ReceiveTask(this, wb.bufferPool.GetBuffer());
            }

            _sendSegments = new PooledSegment[MaxPendingSends];
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
        const RIO_SEND_FLAGS MessageEnd = RIO_SEND_FLAGS.NONE;

        int _currentOffset = 0;
        public void QueueSend(ArraySegment<byte> buffer, bool isEnd)
        {
            var segment = _sendSegments[_sendCount & SendMask];
            var count = buffer.Count;
            var offset = buffer.Offset;

            while (count > 0)
            {
                var length = count >= RIOBufferPool.PacketSize - _currentOffset ? RIOBufferPool.PacketSize - _currentOffset : count;
                Buffer.BlockCopy(buffer.Array, offset, segment.Buffer, segment.Offset + _currentOffset, length);

                if (_currentOffset == RIOBufferPool.PacketSize)
                {
                    segment.RioBuffer.Length = RIOBufferPool.PacketSize;
                    _rio.Send(_requestQueue, &segment.RioBuffer, 1, MessageEnd, -_sendCount - 1);
                    _currentOffset = 0;
                    _sendCount++;
                    segment = _sendSegments[_sendCount & SendMask];
                }
                else if (_currentOffset > RIOBufferPool.PacketSize)
                {
                    throw new Exception("Overflowed buffer");
                }
                else
                {
                    _currentOffset += length;
                }
                offset += length;
                count -= length;
            }
            if (isEnd)
            {
                if (_currentOffset > 0)
                {
                    segment.RioBuffer.Length = (uint)_currentOffset;
                    _rio.Send(_requestQueue, &segment.RioBuffer, 1, MessageEnd, -_sendCount - 1);
                    _currentOffset = 0;
                    _sendCount++;
                }
                else
                {
                    _rio.Send(_requestQueue, null, 1, RIO_SEND_FLAGS.COMMIT_ONLY, 0);
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

        public ReceiveTask ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
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

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //_receiveTask.Dispose();
                }

                TcpConnection connection;
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

        ~TcpConnection()
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
