// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Crank.PerfLabExporter.Contracts;
using Crank.PerfLabExporter.Contracts.Crank;
using Crank.PerfLabExporter.Contracts.Identity;
using Crank.PerfLabExporter.Contracts.Policy;

namespace Crank.PerfLabExporter.Tests
{
    internal static class FixtureLoader
    {
        private static readonly JsonSerializerOptions SerializerOptions =
            ContractJson.CreateSerializerOptions();

        public static string GetPath(string fileName)
        {
            return Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        }

        public static CrankExecutionResult LoadExecution()
        {
            return Load<CrankExecutionResult>("crank-result.json");
        }

        public static CounterPolicy LoadPolicy()
        {
            return Load<CounterPolicy>("counter-policy.json");
        }

        public static ExportIdentity LoadIdentity()
        {
            return Load<ExportIdentity>("export-identity.json");
        }

        private static T Load<T>(string fileName)
        {
            var json = File.ReadAllText(GetPath(fileName));
            return JsonSerializer.Deserialize<T>(json, SerializerOptions)
                ?? throw new InvalidOperationException($"Fixture '{fileName}' deserialized as null.");
        }
    }
}
