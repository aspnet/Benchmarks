using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BenchmarkClient.Models
{
    public class Job
    {
        public int Id { get; set; }

        public string Command { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public State State { get; set; }

        public string Output { get; set; }

        public string Error { get; set; }
    }
}
