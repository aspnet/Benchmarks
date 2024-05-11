// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Benchmarks.SocketBenchmark.Shared;

namespace System.Net.Benchmarks.SocketBenchmark;

internal class SocketServerOptions : SocketOptions, IBenchmarkServerOptions
{
    public int ListenBacklog { get; set; }

    public override string ToString()
        => $"{base.ToString()}, ListenBacklog: {ListenBacklog}";
}
