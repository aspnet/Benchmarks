using System;
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

        internal string ToMarkdownString()
        {
            return $"| {Scenario} | {OperatingSystem}, {Scheme}, {WebHost} | {DateTimeUtc.ToString("u")} | {Values.Skip(1).First().ToString(NumberFormat)} -> {Values.Last().ToString(NumberFormat)} | {((int)Stdev).ToString(NumberFormat)} |";
        }
    }
}