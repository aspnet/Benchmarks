// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ManagedRIOHttpServer.RegisteredIO
{
    public sealed class RIOReceiveTask : INotifyCompletion, ICriticalNotifyCompletion
    {
        private readonly static Action CALLBACK_RAN = () => { };
        private bool _isCompleted;
        private Action _continuation;

        private uint _bytesTransferred;
        private uint _requestCorrelation;
        private ArraySegment<byte> _buffer;
        internal RIOPooledSegment _segment;
        private RIOTcpConnection _connection;

        public RIOReceiveTask(RIOTcpConnection connection, RIOPooledSegment segment)
        {
            _segment = segment;
            _connection = connection;
        }

        internal void Reset()
        {
            _bytesTransferred = 0;
            _isCompleted = false;
            _continuation = null;
        }
        internal void SetBuffer(ArraySegment<byte> buffer)
        {
            _buffer = buffer;
        }
        internal void Complete(uint bytesTransferred, uint requestCorrelation)
        {
            _bytesTransferred = bytesTransferred;
            _requestCorrelation = requestCorrelation;
            _isCompleted = true;

            Action continuation = _continuation ?? Interlocked.CompareExchange(ref _continuation, CALLBACK_RAN, null);
            if (continuation != null)
            {
                continuation();
            }
        }

        public RIOReceiveTask GetAwaiter() { return this; }

        public bool IsCompleted { get { return _isCompleted; } }

        private void UnsafeCallback(object state)
        {
            ((Action)state)();
        }

        public void OnCompleted(Action continuation)
        {
            throw new NotImplementedException();
        }

        [System.Security.SecurityCritical]
        public void UnsafeOnCompleted(Action continuation)
        {
            if (_continuation == CALLBACK_RAN ||
                    Interlocked.CompareExchange(
                        ref _continuation, continuation, null) == CALLBACK_RAN)
            {
                ThreadPool.UnsafeQueueUserWorkItem(UnsafeCallback, continuation);
            }
        }
        public uint GetResult()
        {
            var bytesTransferred = _bytesTransferred;
            Buffer.BlockCopy(_segment.Buffer, _segment.Offset, _buffer.Array, _buffer.Offset, (int)bytesTransferred);
            Reset();
            _connection.PostReceive(_requestCorrelation);
            return bytesTransferred;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        internal void Dispose()
        {
            if (!disposedValue)
            {
                disposedValue = true;
                _segment.Dispose();
            }
        }

        #endregion

    }
}
