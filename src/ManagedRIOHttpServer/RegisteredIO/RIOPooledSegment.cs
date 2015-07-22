// Copyright (c) Illyriad Games. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace ManagedRIOHttpServer.RegisteredIO
{
    public struct RIOPooledSegment : IDisposable
    {
        public readonly byte[] Buffer;
        internal RIO_BUFSEGMENT RioBuffer;
        public readonly int PoolIndex;
        private RIOBufferPool _owningPool;
        internal RIOPooledSegment(int index, RIOBufferPool owningPool, RIO_BUFSEGMENT segment, byte[] buffer)
        {
            PoolIndex = index;
            _owningPool = owningPool;
            RioBuffer = segment;
            Buffer = buffer;
        }

        public int Offset
        {
            get
            {
                return (int)RioBuffer.Offset;
            }
        }

        #region IDisposable Support
        public void Dispose()
        {
            _owningPool.ReleaseBuffer(PoolIndex);
        }
        #endregion
    }
}
