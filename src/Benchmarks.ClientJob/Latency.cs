// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Benchmarks.ClientJob
{
    public class Latency
    {
        public TimeSpan Average { get; set; }
        public TimeSpan Within50thPercentile { get; set; }
        public TimeSpan Within75thPercentile { get; set; }
        public TimeSpan Within90thPercentile { get; set; }
        public TimeSpan Within99thPercentile { get; set; }
    }
}
