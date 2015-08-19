// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace ManagedRIOHttpServer.RegisteredIO
{
    public sealed class RIOBufferPool : IDisposable
    {
        RIO_BUFSEGMENT[] _segments;
        private byte[] _underlyingBuffer;
        public const int PacketSize = (1500 - (20 + 20) + 12) * 4; // MTU - (IPv4 Header + TCP Header) + Round to 64 divisor
        private const int PooledPacketSize = PacketSize + 64; // 32 + PacketSize + 32 w false sharing cache guard bytes
        private const int PerAllocationCount = RIOThreadPool.PreAllocSocketsPerThread * (RIOTcpConnection.MaxPendingReceives + RIOTcpConnection.MaxPendingSends);
        private const int BufferLength = (PooledPacketSize) * PerAllocationCount; // Amount to pin per alloc 9.4 MB ish; into LOH

        private ConcurrentQueue<int> _availableSegments;
        private ConcurrentQueue<AllocatedBuffer> _allocatedBuffers;
        private RIO _rio;

        private struct AllocatedBuffer
        {
            public byte[] Buffer;
            public GCHandle PinnedBuffer;
            public IntPtr BufferId;
        }

        public RIOBufferPool(RIO rio)
        {
            _rio = rio;
            _allocatedBuffers = new ConcurrentQueue<AllocatedBuffer>();
            _availableSegments = new ConcurrentQueue<int>();

            _underlyingBuffer = new byte[BufferLength];
            
        }

        public void Initalize()
        {

            var pinnedBuffer = GCHandle.Alloc(_underlyingBuffer, GCHandleType.Pinned);
            var address = Marshal.UnsafeAddrOfPinnedArrayElement(_underlyingBuffer, 0);
            var bufferId = _rio.RegisterBuffer(address, BufferLength);

            _allocatedBuffers.Enqueue(new AllocatedBuffer() { Buffer = _underlyingBuffer, PinnedBuffer = pinnedBuffer, BufferId = bufferId });

            _segments = new RIO_BUFSEGMENT[PerAllocationCount];
            _availableSegments = new ConcurrentQueue<int>();
            var offset = 0u;
            for (var i = 0; i < _segments.Length; i++)
            {
                _segments[i] = new RIO_BUFSEGMENT(bufferId, offset, PacketSize);
                _availableSegments.Enqueue(i);
                offset += PooledPacketSize;
            }

        }

        public RIOPooledSegment GetBuffer()
        {
            int bufferNo;
            if (_availableSegments.TryDequeue(out bufferNo))
            {
                return new RIOPooledSegment(bufferNo, this, _segments[bufferNo], _underlyingBuffer);
            }
            else
            {
                throw new NotImplementedException("Out of pooled buffers; not implemented dynamic expansion");
            }
        }
        internal void ReleaseBuffer(int bufferIndex)
        {
            _availableSegments.Enqueue(bufferIndex);
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                AllocatedBuffer buffer;
                while (_allocatedBuffers.TryDequeue(out buffer))
                {
                    _rio.DeregisterBuffer(buffer.BufferId);
                    buffer.PinnedBuffer.Free();
                }

                if (disposing)
                {
                    _segments = null;
                    _underlyingBuffer = null;
                    _rio = null;
                    _availableSegments = null;
                    _allocatedBuffers = null;
                }

                disposedValue = true;
            }
        }

        ~RIOBufferPool()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }

}
