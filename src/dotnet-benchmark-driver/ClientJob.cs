using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BenchmarkDriver
{
    public class ClientJob
    {
        public int Id { get; set; }

        public string Command { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ClientState State { get; set; }

        public string Output { get; set; }

        public string Error { get; set; }
    }
}
