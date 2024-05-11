// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace System.Net.Benchmarks;

internal class SingleStreamConnection<TStream> : IConnection
    where TStream : Stream
{
    internal SingleStreamConnection()
    {

    }

    internal SingleStreamConnection(TStream innerStream)
    {
        InnerStream = innerStream;
    }
    private int _streamConsumed;
    protected TStream? InnerStream { get; set; }

    public bool IsMultiplexed => false;

    protected TStream ConsumeStream()
    {
        if (InnerStream is null)
        {
            throw new InvalidOperationException("InnerStream not initialized");
        }

        if (Interlocked.CompareExchange(ref _streamConsumed, 1, 0) == 1)
        {
            throw new InvalidOperationException("InnerStream already consumed");
        }

        return InnerStream;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _streamConsumed, 1, 0) == 1)
        {
            return default; // we gave away the ownership of the stream
        }
        return InnerStream?.DisposeAsync() ?? default;
    }
}
