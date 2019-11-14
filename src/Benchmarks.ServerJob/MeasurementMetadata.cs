using System;

namespace Benchmarks.ServerJob
{
    public class MeasurementMetadata
    {
        public string Source { get; set; }
        public string Name { get; set; }

        // how to aggregate the value across multiple clients returning the same measurement
        public Operation Reduce { get; set; }

        // how to render the value from many measures in the same client
        public Operation Aggregate { get; set; }

        public string ShortDescription { get; set; }
        public string LongDescription { get; set; }
        public string Format { get; set; }
    }

    public enum Operation
    {
        Avg,
        Sum,
        Median,
        Max,
        Min,
        Count
    }
}
