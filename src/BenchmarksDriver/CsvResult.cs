using CsvHelper.Configuration;

namespace BenchmarksDriver
{
    public class CsvResult
    {
        public string Method { get; set; }
        public double Mean { get; set; }
        public double Error { get; set; }
        public double StdDev { get; set; }
        public double OperationsPerSecond { get; set; }
        public double Allocated { get; set; }
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
