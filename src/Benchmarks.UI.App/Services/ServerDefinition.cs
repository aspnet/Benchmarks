using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Benchmarks.UI.App.Services
{
    public class ServerDefinition
    {
        public ServerDefinition()
        {

        }

        public ServerDefinition(string displayName, string arguments)
        {
            DisplayName = displayName;
            Arguments = arguments;
        }

        public string DisplayName { get; set; }
        public string Arguments { get; set; }
    }
}
