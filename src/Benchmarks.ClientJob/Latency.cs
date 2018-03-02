// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Benchmarks.ClientJob
{
    public class Latency
    {
        public double Average { get; set; } = -1;
        public double Within50thPercentile { get; set; } = -1;
        public double Within75thPercentile { get; set; } = -1;
        public double Within90thPercentile { get; set; } = -1;
        public double Within99thPercentile { get; set; } = -1;
    }
}
