using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Benchmarks.ServerJob
{
    public class MeasurementMetadata
    {
        public string Source { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// An operation used to aggregate the value across multiple sources returning the same measurement
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public Operation Reduce { get; set; }

        /// <summary>
        /// An operation used to aggregate the measures from the same source
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public Operation Aggregate { get; set; }

        public string ShortDescription { get; set; }
        public string LongDescription { get; set; }

        // A custom C# format string used for numerical values, e.g. "n0"
        public string Format { get; set; }
    }

    public enum Operation
    {
        First,
        Last,
        Avg,
        Sum,
        Median,
        Max,
        Min,
        Count,
        All
    }
}
