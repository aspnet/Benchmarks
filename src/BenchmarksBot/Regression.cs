using System;
using System.IO;
using System.Linq;

namespace BenchmarksBot
{
    public class Regression
    {
        static string NumberFormat = "##,#";

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
        public string PreviousRuntimeVersion { get; set; }
        public string CurrentRuntimeVersion { get; set; }

        public string[] AspNetCoreHashes { get; set; }
        public string[] CoreFxHashes { get; set; }
        public string[] CoreClrHashes { get; set; }

        public void WriteTableRow(TextWriter writer)
        {
            writer.WriteLine($"| {Scenario} | {OperatingSystem}, {Scheme}, {WebHost} | {DateTimeUtc.ToString("u")} | {Values.Skip(1).First().ToString(NumberFormat)} -> {Values.Last().ToString(NumberFormat)} | {((int)Stdev).ToString(NumberFormat)} |");
        }
    }
}