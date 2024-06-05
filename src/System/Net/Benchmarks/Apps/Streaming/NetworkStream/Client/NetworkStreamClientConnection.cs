// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Sockets;

namespace System.Net.Benchmarks.NetworkStreamBenchmark.Client;

internal class NetworkStreamClientConnection : SingleStreamConnection<NetworkStream>, IConnection
{
    public NetworkStreamClientConnection(NetworkStream innerStream) => InnerStream = innerStream;
    public ValueTask<NetworkStream> EstablishStreamAsync(NetworkStreamClientOptions options) => ValueTask.FromResult(ConsumeStream());
}
