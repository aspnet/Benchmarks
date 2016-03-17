using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BenchmarkDriver
{
    public class ServerJob
    {
        public int Id { get; set; }

        public string Scenario { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ServerState State { get; set; }

        public string Url { get; set; }
    }
}
