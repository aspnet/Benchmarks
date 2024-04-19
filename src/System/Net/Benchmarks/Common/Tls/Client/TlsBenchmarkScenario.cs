// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace System.Net.Benchmarks.Tls;

public enum TlsBenchmarkScenario
{
    // Measure throughput
    ReadWrite,

    // measure number of handshakes per second
    Handshake,

    // measure number of requests per second
    Rps
}
