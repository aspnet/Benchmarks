// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Benchmarks.NetworkStreamBenchmark.Shared;

namespace System.Net.Benchmarks.NetworkStreamBenchmark.Client;

internal class NetworkStreamClientOptions : NetworkStreamOptions, IBenchmarkClientOptions
{
    public TimeSpan Warmup { get; set; }

    public TimeSpan Duration { get; set; }
    public int Connections { get; set; }
    public IPAddress? Address { get; set; }

    public override string ToString() => $"{base.ToString()}, Warmup: {Warmup}, Duration: {Duration}, Connections: {Connections}, Address: {Address}";
}
