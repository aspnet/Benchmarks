using System;

namespace Benchmarks.ServerJob
{
    public class Measurement
    {
        public DateTime Timestamp { get; set; }
        public string Name { get; set; }
        public object Value { get; set; }
    }
}
