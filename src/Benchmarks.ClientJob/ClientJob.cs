using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Repository;

namespace Benchmarks.ClientJob
{
    public class ClientJob : IIdentifiable
    {
        public int Id { get; set; }

        public string Command { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ClientState State { get; set; }

        public string Output { get; set; }

        public string Error { get; set; }
    }
}
