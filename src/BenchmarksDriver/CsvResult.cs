using System;
using System.Collections.Generic;
using System.Text;
using CsvHelper.Configuration;

namespace BenchmarksDriver
{
    public class CsvResult
    {
        public string Method { get; set; }
        public float Mean { get; set; }
        public float Error { get; set; }
        public float StdDev { get; set; }
        public float OperationsPerSecond { get; set; }
        public float Allocated { get; set; }
    }

    public sealed class CsvResultMap : ClassMap<CsvResult>
    {
        public CsvResultMap()
        {
            Map(m => m.Method).Name("Method");
            Map(m => m.Mean).Name("Mean [us]");
            Map(m => m.Error).Name("Error [us]");
            Map(m => m.StdDev).Name("StdDev [us]");
            Map(m => m.OperationsPerSecond).Name("Op/s");
            Map(m => m.Allocated).Name("Allocated [KB]");
        }
    }
}
