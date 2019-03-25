// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Benchmarks.ClientJob
{
    public enum Worker
    {
        Wrk,
        Wrk2,
        SignalR,
        Wait,
        H2Load,
        Grpc,
        None,
        BenchmarkDotNet,
        Bombardier
    }
}
