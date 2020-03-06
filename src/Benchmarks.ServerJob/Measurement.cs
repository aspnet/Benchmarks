using System;

namespace Benchmarks.ServerJob
{
    public class Measurement
    {
        public const string Delimiter = "$$Delimiter$$";

        public DateTime Timestamp { get; set; }
        public string Name { get; set; }
        public object Value { get; set; }
        public bool IsDelimiter => Name == Delimiter;
    }
}
