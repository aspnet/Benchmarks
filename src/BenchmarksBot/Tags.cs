using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BenchmarksBot
{
    /// <summary>
    /// Contains the mapping between scenario names and GitHub labels to assign to issues related to each scenario.
    /// </summary>
    public class Tags
    {
        public static readonly Dictionary<string, Tags> Scenarios = new Dictionary<string, Tags>(StringComparer.OrdinalIgnoreCase)
        {
            {"^BasicViews.GetTagHelpers$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^BasicViews.Post$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^ConnectionClose$", new Tags { Labels = new [] { "area-servers" } } },
            {"^DbFortunesDapper$", new Tags { Labels = new [] { "" } } },
            {"^DbFortunesEf$", new Tags { Labels = new [] { "" } } },
            {"^DbFortunesRaw$", new Tags { Labels = new [] { "" } } },
            {"^DbMultiQueryDapper$", new Tags { Labels = new [] { "" } } },
            {"^DbMultiQueryEf$", new Tags { Labels = new [] { "" } } },
            {"^DbMultiQueryRaw$", new Tags { Labels = new [] { "" } } },
            {"^DbMultiUpdateDapper$", new Tags { Labels = new [] { "" } } },
            {"^DbMultiUpdateEf$", new Tags { Labels = new [] { "" } } },
            {"^DbMultiUpdateRaw$", new Tags { Labels = new [] { "" } } },
            {"^DbSingleQueryDapper$", new Tags { Labels = new [] { "" } } },
            {"^DbSingleQueryEf$", new Tags { Labels = new [] { "" } } },
            {"^DbSingleQueryRaw$", new Tags { Labels = new [] { "" } } },
            {"^EndpointPlaintext$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^FortunesPostgreSql-Actix$", new Tags { Labels = new [] { "" } } },
            {"^FortunesPostgreSql-FastHttp$", new Tags { Labels = new [] { "" } } },
            {"^FortunesPostgreSql-NodeJs$", new Tags { Labels = new [] { "" } } },
            {"^HttpClient$", new Tags { Labels = new [] { "area-httpclientfactory" } } },
            {"^Json$", new Tags { Labels = new [] { "" } } },
            {"^Json-Actix$", new Tags { Labels = new [] { "" } } },
            {"^Json-FastHttp$", new Tags { Labels = new [] { "" } } },
            {"^Json-NodeJs$", new Tags { Labels = new [] { "" } } },
            {"^JsonPlatform$", new Tags { Labels = new [] { "" } } },
            {"^LinkAThonBaseline$", new Tags { Labels = new [] { "" } } },
            {"^LinkAThonTrimmedAndR2R$", new Tags { Labels = new [] { "" } } },
            {"^LinkAThonTrimmedAndR2RSingleFile$", new Tags { Labels = new [] { "" } } },
            {"^LinkAThonTrimmedAndR2RSingleFileWithTrimList$", new Tags { Labels = new [] { "" } } },
            {"^LinkAThonTrimmedAndR2RSingleFileNoMvc$", new Tags { Labels = new [] { "" } } },
            {"^LinkAThonTrimmedAndR2RSingleFileCustomHost$", new Tags { Labels = new [] { "" } } },
            {"^LinkAThonPlatformBaseline$", new Tags { Labels = new [] { "" } } },
            {"^LinkAThonGrpcBaseline$", new Tags { Labels = new [] { "area-grpc" } } },
            {"^LinkAThonGrpcTrimmedAndR2R$", new Tags { Labels = new [] { "area-grpc" } } },
            {"^LinkAThonGrpcTrimmedAndR2RSingleFile$", new Tags { Labels = new [] { "area-grpc" } } },
            {"^MemoryCachePlaintext$", new Tags { Labels = new [] { "" } } },
            {"^MemoryCachePlaintextSetRemove$", new Tags { Labels = new [] { "" } } },
            {"^MvcDbFortunesDapper$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcDbFortunesEf$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcDbFortunesRaw$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcDbMultiQueryDapper$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcDbMultiQueryEf$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcDbMultiQueryRaw$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcDbMultiUpdateDapper$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcDbMultiUpdateEf$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcDbMultiUpdateRaw$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcDbSingleQueryDapper$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcDbSingleQueryEf$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcDbSingleQueryRaw$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJson$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJson2k$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJsonInput2k$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJsonNet$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJsonNet2k$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJsonNetInput2k$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJsonInput2M$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJsonOutput2M$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJsonNetInput2M$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJsonNetOutput2M$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJsonInput60k$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJsonOutput60k$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJsonNetInput60k$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcJsonNetOutput60k$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^MvcPlaintext$", new Tags { Labels = new [] { "area-mvc" } } },
            {"^OrchardBlog$", new Tags { Labels = new [] { "" }, Owners = new [] { "sebastienros" } } },
            {"^OrchardBlogAbout$", new Tags { Labels = new [] { "" }, Owners = new [] { "sebastienros" } } },
            {"^OrchardBlogPost$", new Tags { Labels = new [] { "" }, Owners = new [] { "sebastienros" } } },
            {"^Plaintext$", new Tags { Labels = new [] { "area-servers" } } },
            {"^Plaintext-Actix$", new Tags { Labels = new [] { "" } } },
            {"^Plaintext-FastHttp$", new Tags { Labels = new [] { "" } } },
            {"^Plaintext-NodeJs$", new Tags { Labels = new [] { "" } } },
            {"^PlaintextNonPipelined$", new Tags { Labels = new [] { "area-servers" } } },
            {"^PlaintextNonPipelinedLogging$", new Tags { Labels = new [] { "" } } },
            {"^PlaintextNonPipelinedLoggingNoScopes$", new Tags { Labels = new [] { "" } } },
            {"^PlaintextPlatform$", new Tags { Labels = new [] { "" } } },
            {"^ResponseCaching", new Tags { Labels = new [] { "area-middleware" } } },
            {"^SignalR", new Tags { Labels = new [] { "area-signalr" } } },
            {"^StaticFiles$", new Tags { Labels = new [] { "area-middleware" } } },
            {"^Basic$", new Tags { Labels = new [] { "area-blazor" }, Owners = new [] { "pranavkm" }, } },
            {"^FormInput$", new Tags { Labels = new [] { "area-blazor" }, Owners = new [] { "pranavkm" } } },
            {"Mono$", new Tags { IgnoreRegressions = true } },
            {"^GRPC", new Tags { Labels = new [] { "area-grpc" }, Owners = new [] { "JamesNK" } } },
        };

        /// <summary>
        /// Returns the list of <see cref="Tags" /> that match a scenario.
        /// </summary>
        public static IEnumerable<Tags> Match(string scenario)
        {
            foreach(var tag in Scenarios)
            {
                if (new Regex(tag.Key).IsMatch(scenario))
                {
                    yield return tag.Value;
                }
            }
        }

        public static bool ReportRegressions(string scenario)
        {
            foreach (var tag in Scenarios)
            {
                if (new Regex(tag.Key).IsMatch(scenario))
                {
                    if (tag.Value.IgnoreRegressions)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Gets or sets the list of labels to assign to the issues
        /// </summary>
        public string[] Labels { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the list of users that are cced in the issue
        /// </summary>
        public string[] Owners = Array.Empty<string>();

        /// <summary>
        /// Gets or sets whether the regressions on the scenario should be ignored
        /// </summary>
        public bool IgnoreRegressions { get; set; }
    }
}
