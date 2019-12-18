using System.Collections.Generic;

namespace Benchmarks.ServerJob
{
    public class JobStatistics
    {
        public List<MeasurementMetadata> Metadata = new List<MeasurementMetadata>();
        public List<Measurement> Measurements = new List<Measurement>();
    }
}
