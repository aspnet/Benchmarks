using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace Benchmarks
{
    public class Scenarios
    {
        public bool Plaintext { get; set; }

        public bool Json { get; set; }

        public bool StaticFiles { get; set; }

        public bool MvcApis { get; set; }

        public bool MvcViews { get; set; }

        public bool DbSingleQueryRaw { get; set; }

        public bool DbSingleQueryEf { get; set; }

        public bool DbSingleQueryDapper { get; set; }

        public bool DbMultiQueryRaw { get; set; }

        public bool DbMultiQueryEf { get; set; }

        public bool DbMultiQueryDapper { get; set; }

        public bool DbFortunesRaw { get; set; }

        public bool DbFortunesEf { get; set; }

        public bool DbFortunesDapper { get; set; }

        public bool Any(string partialName) =>
            typeof(Scenarios).GetTypeInfo().DeclaredProperties
                .Where(p => p.Name.IndexOf(partialName, StringComparison.Ordinal) >= 0 && (bool)p.GetValue(this))
                .Any();

        public IEnumerable<string> GetNames() =>
            typeof(Scenarios).GetTypeInfo().DeclaredProperties
                .Select(p => p.Name);

        public IEnumerable<string> GetEnabled() =>
            typeof(Scenarios).GetTypeInfo().DeclaredProperties
                .Where(p => p.GetValue(this) is bool && (bool)p.GetValue(this))
                .Select(p => p.Name);

        public int Enable(string partialName)
        {
            var props = typeof(Scenarios).GetTypeInfo().DeclaredProperties
                .Where(p => string.Equals(partialName, "[all]", StringComparison.OrdinalIgnoreCase) || p.Name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            foreach (var p in props)
            {
                p.SetValue(this, true);
            }
            
            return props.Count;
        }
        
        public void EnableDefault()
        {
            Plaintext = true;
            Json = true;
        }
    }
}