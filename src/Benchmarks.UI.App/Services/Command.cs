using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using Newtonsoft.Json;

namespace Benchmarks.UI.App.Services
{
    public class Command
    {
        public string CurrentServer { get; set; }
        public string Scenario { get; set; }
        public string Warmup { get; set; }
        public string Duration { get; set; }
        public string Samples { get; set; }
        public string Extremes { get; set; }
        public string Database { get; set; }
        public string Host { get; set; }
        public bool Quiet { get; set; }
        public bool Markdown { get; set; }
        public string AspNetCore { get; set; }
        public string Runtime { get; set; }
        public Dictionary<string, string> OutputFiles { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> NugetPackages { get; set; } = new Dictionary<string, string>();
    }
}
