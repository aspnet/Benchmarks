// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace System.Net.Benchmarks;

internal interface IBenchmarkClientOptions
{
    TimeSpan Warmup { get; }
    TimeSpan Duration { get; }
}
