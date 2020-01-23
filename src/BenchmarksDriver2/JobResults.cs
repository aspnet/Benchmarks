using System.Collections.Generic;
using Benchmarks.ServerJob;

namespace BenchmarksDriver
{
    public class JobResults
    {
        public Dictionary<string, JobResult> Jobs { get; set; } = new Dictionary<string, JobResult>();
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }

    public class JobResult
    {
        public Dictionary<string, object> Results { get; set; }
        public MeasurementMetadata[] Metadata { get; set; }
        public List<Measurement[]> Measurements { get; set; } = new List<Measurement[]>();
        public Dictionary<string, object> Environment = new Dictionary<string, object>();
    }
}
