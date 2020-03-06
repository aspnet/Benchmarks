using System;
using Newtonsoft.Json;

namespace Benchmarks.ServerJob
{
    public class Measurement
    {
        public const string Delimiter = "$$Delimiter$$";

        public DateTime Timestamp { get; set; }
        public string Name { get; set; }
        public object Value { get; set; }

        [JsonIgnore]
        public bool IsDelimiter => String.Equals(Name, Delimiter, StringComparison.OrdinalIgnoreCase);
    }
}
