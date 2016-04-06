using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Repository;

namespace Benchmarks.ServerJob
{
    public class ServerJob : IIdentifiable
    {
        public int Id { get; set; }

        public string BenchmarksRepo { get; set; }

        public string BenchmarksBranch { get; set; }

        public string KestrelRepo { get; set; }

        public string KestrelBranch { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public Scenario Scenario { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ServerState State { get; set; }

        public string Url { get; set; }
    }
}
