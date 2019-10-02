using System;
using System.Collections.Generic;

namespace BenchmarksBot
{
    /// <summary>
    /// Contains the mapping between scenario names and GitHub labels to assign to issues related to each scenario.
    /// </summary>
    public class Tags
    {
        public static readonly Dictionary<string, Tags> Scenarios = new Dictionary<string, Tags>(StringComparer.OrdinalIgnoreCase)
        {
            {"BasicViews.GetTagHelpers", new Tags { Labels = new [] { "area-mvc" } } },
            {"BasicViews.Post", new Tags { Labels = new [] { "area-mvc" } } },
            {"ConnectionClose", new Tags { Labels = new [] { "area-servers" } } },
            {"DbFortunesDapper", new Tags { Labels = new [] { "" } } },
            {"DbFortunesEf", new Tags { Labels = new [] { "" } } },
            {"DbFortunesRaw", new Tags { Labels = new [] { "" } } },
            {"DbMultiQueryDapper", new Tags { Labels = new [] { "" } } },
            {"DbMultiQueryEf", new Tags { Labels = new [] { "" } } },
            {"DbMultiQueryRaw", new Tags { Labels = new [] { "" } } },
            {"DbMultiUpdateDapper", new Tags { Labels = new [] { "" } } },
            {"DbMultiUpdateEf", new Tags { Labels = new [] { "" } } },
            {"DbMultiUpdateRaw", new Tags { Labels = new [] { "" } } },
            {"DbSingleQueryDapper", new Tags { Labels = new [] { "" } } },
            {"DbSingleQueryEf", new Tags { Labels = new [] { "" } } },
            {"DbSingleQueryRaw", new Tags { Labels = new [] { "" } } },
            {"EndpointPlaintext", new Tags { Labels = new [] { "area-mvc" } } },
            {"FortunesPostgreSql-Actix", new Tags { Labels = new [] { "" } } },
            {"FortunesPostgreSql-FastHttp", new Tags { Labels = new [] { "" } } },
            {"FortunesPostgreSql-NodeJs", new Tags { Labels = new [] { "" } } },
            {"HttpClient", new Tags { Labels = new [] { "area-httpclientfactory" } } },
            {"HttpClientFactory", new Tags { Labels = new [] { "area-httpclientfactory" } } },
            {"HttpClientParallel", new Tags { Labels = new [] { "area-httpclientfactory" } } },
            {"Json", new Tags { Labels = new [] { "" } } },
            {"Json-Actix", new Tags { Labels = new [] { "" } } },
            {"Json-FastHttp", new Tags { Labels = new [] { "" } } },
            {"Json-NodeJs", new Tags { Labels = new [] { "" } } },
            {"JsonPlatform", new Tags { Labels = new [] { "" } } },
            {"LinkAThonBaseline", new Tags { Labels = new [] { "" } } },
            {"LinkAThonTrimmedAndR2R", new Tags { Labels = new [] { "" } } },
            {"LinkAThonTrimmedAndR2RSingleFile", new Tags { Labels = new [] { "" } } },
            {"LinkAThonTrimmedAndR2RSingleFileWithTrimList", new Tags { Labels = new [] { "" } } },
            {"LinkAThonTrimmedAndR2RSingleFileNoMvc", new Tags { Labels = new [] { "" } } },
            {"LinkAThonTrimmedAndR2RSingleFileCustomHost", new Tags { Labels = new [] { "" } } },
            {"LinkAThonPlatformBaseline", new Tags { Labels = new [] { "" } } },
            {"LinkAThonGrpcBaseline", new Tags { Labels = new [] { "area-grpc" } } },
            {"LinkAThonGrpcTrimmedAndR2R", new Tags { Labels = new [] { "area-grpc" } } },
            {"LinkAThonGrpcTrimmedAndR2RSingleFile", new Tags { Labels = new [] { "area-grpc" } } },
            {"MemoryCachePlaintext", new Tags { Labels = new [] { "" } } },
            {"MemoryCachePlaintextSetRemove", new Tags { Labels = new [] { "" } } },
            {"MvcDbFortunesDapper", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcDbFortunesEf", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcDbFortunesRaw", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcDbMultiQueryDapper", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcDbMultiQueryEf", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcDbMultiQueryRaw", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcDbMultiUpdateDapper", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcDbMultiUpdateEf", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcDbMultiUpdateRaw", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcDbSingleQueryDapper", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcDbSingleQueryEf", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcDbSingleQueryRaw", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcJson", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcJson2k", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcJsonInput2k", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcJsonNet", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcJsonNet2k", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcJsonNetInput2k", new Tags { Labels = new [] { "area-mvc" } } },
            {"MvcPlaintext", new Tags { Labels = new [] { "area-mvc" } } },
            {"OrchardBlog", new Tags { Labels = new [] { "" } } },
            {"Plaintext", new Tags { Labels = new [] { "area-servers" } } },
            {"Plaintext-Actix", new Tags { Labels = new [] { "" } } },
            {"Plaintext-FastHttp", new Tags { Labels = new [] { "" } } },
            {"Plaintext-NodeJs", new Tags { Labels = new [] { "" } } },
            {"PlaintextNonPipelined", new Tags { Labels = new [] { "area-servers" } } },
            {"PlaintextNonPipelinedLogging", new Tags { Labels = new [] { "" } } },
            {"PlaintextNonPipelinedLoggingNoScopes", new Tags { Labels = new [] { "" } } },
            {"PlaintextPlatform", new Tags { Labels = new [] { "" } } },
            {"ResponseCachingPlaintextCached", new Tags { Labels = new [] { "area-middleware" } } },
            {"ResponseCachingPlaintextResponseNoCache", new Tags { Labels = new [] { "area-middleware" } } },
            {"ResponseCachingPlaintextRequestNoCache", new Tags { Labels = new [] { "area-middleware" } } },
            {"ResponseCachingPlaintextVaryByCached", new Tags { Labels = new [] { "area-middleware" } } },
            {"StaticFiles", new Tags { Labels = new [] { "area-middleware" } } },
            {"Basic", new Tags { Labels = new [] { "area-blazor" }, Owners = new [] { "pranavkm" }, } },
            {"FormInput", new Tags { Labels = new [] { "area-blazor" }, Owners = new [] { "pranavkm" } } },
        };

        /// <summary>
        /// Gets or sets the list of labels to assign to the issues
        /// </summary>
        public string[] Labels { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the list of users that are cced in the issue
        /// </summary>
        public string[] Owners = Array.Empty<string>();
    }
}
