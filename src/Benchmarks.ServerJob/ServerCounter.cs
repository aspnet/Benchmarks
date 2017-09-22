// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Benchmarks.ServerJob
{
    public class ServerCounter
    {
        public TimeSpan Elapsed { get; set; }
        public long WorkingSet { get; set; }
        public double CpuPercentage { get; set; }
    }
}
