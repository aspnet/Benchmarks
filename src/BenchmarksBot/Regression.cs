﻿using System;

namespace BenchmarksBot
{
    public class Regression
    {
        public DateTimeOffset DateTimeUtc { get; set; }
        public string Scenario { get; set; }
        public string Hardware { get; set; }
        public string OperatingSystem { get; set; }
        public string Scheme { get; set; }
        public string WebHost { get; set; }
        public int[] Values { get; set; }
        public double Stdev { get; set; }
        public string Session { get; set; }
        public string PreviousAspNetCoreVersion { get; set; }
        public string CurrentAspNetCoreVersion { get; set; }
        public string PreviousDotnetCoreVersion { get; set; }
        public string CurrentDotnetCoreVersion { get; set; }
        public int Errors { get; set; }

        public string[] AspNetCoreHashes { get; set; }
        public string[] DotnetCoreHashes { get; set; }
    }
}