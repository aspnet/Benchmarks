// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Sockets;
using System.Net.Benchmarks.NetworkStreamBenchmark.Shared;

namespace System.Net.Benchmarks.NetworkStreamBenchmark;

internal class NetworkStreamServerConnection : SingleStreamConnection<NetworkStream>, IConnection
{
    internal NetworkStreamServerConnection(NetworkStream innerStream)
    {
        InnerStream = innerStream;
    }

    public ValueTask<NetworkStream> EstablishStreamAsync() => ValueTask.FromResult(ConsumeStream());
}
