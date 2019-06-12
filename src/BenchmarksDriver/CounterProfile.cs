using System;
using System.Collections.Generic;
using System.Linq;

namespace BenchmarksDriver
{
    public class CounterProfile
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Format { get; set; }
        public Func<IEnumerable<double>, double> Compute { get; set; }
    }
}
