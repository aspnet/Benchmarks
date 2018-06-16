// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Benchmarks.ServerJob
{
    public class Source
    {
        public string BranchOrCommit { get; set; }
        public string Repository { get; set; }
        public string Project { get; set; }

        public string DockerFile { get; set; }
        public string DockerImageName { get; set; }
        public string DockerContextDirectory { get; set; }

        // When set, contains the location of the uploaded source code
        public Attachment SourceCode { get; set; }
    }
}
