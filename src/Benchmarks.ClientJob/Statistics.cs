// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Benchmarks.ClientJob
{
    public class Statistics
    {
        public string Description { get; set; }
        public double RequestsPerSecond { get; set; }
        public double LatencyOnLoad { get; set; }
        public double Cpu { get; set; }
        public double WorkingSet { get; set; }
        public double StartupMain { get; set; }
        public double FirstRequest { get; set; }
        public double Latency { get; set; }
        public double SocketErrors { get; set; }
        public double BadResponses { get; set; }
        public double LatencyAverage { get; set; }
        public double Latency50Percentile { get; set; }
        public double Latency75Percentile { get; set; }
        public double Latency90Percentile { get; set; }
        public double Latency99Percentile { get; set; }
        public double MaxLatency { get; set; }
        public double TotalRequests { get; set; }
        public double Duration { get; set; }
        public Dictionary<string, double> Other = new Dictionary<string, double>();
    }
}
