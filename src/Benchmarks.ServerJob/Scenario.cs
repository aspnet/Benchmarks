// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Benchmarks.ServerJob
{
    /// <summary>
    /// The scenario is used both as a commandline argument to the Benchmarks process and also to compute
    /// the path portion of the URL. These definitions should match those in the Benchmarks process.
    ///
    /// Use [ScenarioPath(...)] when the path portion of the URL doesn't match the scenario name exactly.
    /// </summary>
    public enum Scenario
    {
        Plaintext,
        Json,
        CopyToAsync,

        [ScenarioPath("/jil")]
        Jil,

        [ScenarioPath("/mvc/plaintext")]
        MvcPlaintext,

        [ScenarioPath("/mvc/json")]
        MvcJson,

        [ScenarioPath("/mvc/jil")]
        MvcJil,

        [ScenarioPath("/memorycache/plaintext")]
        MemoryCachePlaintext,

        [ScenarioPath("/memorycache/plaintext/setremove")]
        MemoryCachePlaintextSetRemove,

        [ScenarioPath("/responsecaching/plaintext/cached")]
        ResponseCachingPlaintextCached,

        [ScenarioPath("/responsecaching/plaintext/responsenocache")]
        ResponseCachingPlaintextResponseNoCache,

        [ScenarioPath("/responsecaching/plaintext/requestnocache")]
        ResponseCachingPlaintextRequestNoCache,

        [ScenarioPath("/responsecaching/plaintext/varybycached")]
        ResponseCachingPlaintextVaryByCached,

        [ScenarioPath("/plaintext", "/128B.txt", "/512B.txt", "/1KB.txt", "/4KB.txt", "/16KB.txt", "/512KB.txt", "/1MB.txt", "/5MB.txt")]
        StaticFiles,

        [ScenarioPath("/db/raw")]
        DbSingleQueryRaw,

        [ScenarioPath("/db/dapper")]
        DbSingleQueryDapper,

        [ScenarioPath("/db/ef")]
        DbSingleQueryEf,

        [ScenarioPath("/mvc/db/raw")]
        MvcDbSingleQueryRaw,

        [ScenarioPath("/mvc/db/dapper")]
        MvcDbSingleQueryDapper,

        [ScenarioPath("/mvc/db/ef")]
        MvcDbSingleQueryEf,

        [ScenarioPath("/queries/raw")]
        DbMultiQueryRaw,

        [ScenarioPath("/queries/dapper")]
        DbMultiQueryDapper,

        [ScenarioPath("/queries/ef")]
        DbMultiQueryEf,

        [ScenarioPath("/mvc/queries/raw")]
        MvcDbMultiQueryRaw,

        [ScenarioPath("/mvc/queries/dapper")]
        MvcDbMultiQueryDapper,

        [ScenarioPath("/mvc/queries/ef")]
        MvcDbMultiQueryEf,

        [ScenarioPath("/updates/raw")]
        DbMultiUpdateRaw,

        [ScenarioPath("/updates/dapper")]
        DbMultiUpdateDapper,

        [ScenarioPath("/updates/ef")]
        DbMultiUpdateEf,

        [ScenarioPath("/mvc/updates/raw")]
        MvcDbMultiUpdateRaw,

        [ScenarioPath("/mvc/updates/dapper")]
        MvcDbMultiUpdateDapper,

        [ScenarioPath("/mvc/updates/ef")]
        MvcDbMultiUpdateEf,

        [ScenarioPath("/fortunes/raw")]
        DbFortunesRaw,

        [ScenarioPath("/fortunes/dapper")]
        DbFortunesDapper,

        [ScenarioPath("/fortunes/ef")]
        DbFortunesEf,

        [ScenarioPath("/mvc/fortunes/raw")]
        MvcDbFortunesRaw,

        [ScenarioPath("/mvc/fortunes/dapper")]
        MvcDbFortunesDapper,

        [ScenarioPath("/mvc/fortunes/ef")]
        MvcDbFortunesEf,
    }
}
