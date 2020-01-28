// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Octokit;

namespace PRJobProducer
{
    public class PRBenchmarkRequest
    {
        public PullRequest PullRequest { get; set; }
        public string ScenarioName { get; set; }
    }
}
