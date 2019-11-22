using System.Collections.Generic;
using Benchmarks.ServerJob;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BenchmarksDriver
{
    public class Configuration
    {
        public JObject Variables { get; set; } = new JObject();

        public List<string> Dependencies { get; set; } = new List<string>();

        public Dictionary<string, ServerJob> Services { get; set; } = new Dictionary<string, ServerJob>();

        public Dictionary<string, ServerJob> Profiles { get; set; } = new Dictionary<string, ServerJob>();
    }
}
