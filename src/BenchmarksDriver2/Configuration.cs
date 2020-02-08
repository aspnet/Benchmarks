using System.Collections.Generic;
using Benchmarks.ServerJob;

namespace BenchmarksDriver
{
    public class Configuration
    {
        public object Variables { get; set; } = new Dictionary<string, object>();

        public Dictionary<string, ServerJob> Jobs { get; set; } = new Dictionary<string, ServerJob>();

        public Dictionary<string, Dictionary<string, Scenario>> Scenarios { get; set; } = new Dictionary<string, Dictionary<string, Scenario>>();

        public Dictionary<string, object> Profiles { get; set; } = new Dictionary<string, object>();
    }

    public class Scenario
    {
        public string Job { get; set; }
    }
}
