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

        [ScenarioPath("/mvc/plaintext")]
        MvcPlaintext,

        [ScenarioPath("/mvc/json")]
        MvcJson,

        [ScenarioPath("/cached/plaintext")]
        CachedPlaintext,

        [ScenarioPath("/cached/plaintext/nocache")]
        CachedPlaintextNocache,
    }
}
