using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BenchmarksDriver
{
    class JobDefinition : Dictionary<string, JObject>
    {
        public JobDefinition() : base(StringComparer.OrdinalIgnoreCase)
        {
        }
    }
}
