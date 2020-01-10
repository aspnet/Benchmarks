using System.Collections.Generic;
using Benchmarks.ServerJob;
using Newtonsoft.Json.Linq;

namespace BenchmarksDriver
{
    public class Configuration
    {
        public object Variables { get; set; } = new Dictionary<string, object>();

        public Dictionary<string, ServerJob> Jobs { get; set; } = new Dictionary<string, ServerJob>();

        public Dictionary<string, Dictionary<string, Dependency>> Scenarios { get; set; } = new Dictionary<string, Dictionary<string, Dependency>>();
    }

    public class Dependency
    {
        public string Job { get; set; }
        public JObject Variables { get; set; } = new JObject();
    }
}
