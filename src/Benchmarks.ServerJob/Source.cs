// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;

namespace Benchmarks.ServerJob
{
    public class Source
    {
        /// <summary>
        /// The name of a branch, or a commit hash starting with '#'
        /// </summary>
        public string BranchOrCommit { get; set; } = "";
        public string Repository { get; set; }
        public string Project { get; set; }
        public bool InitSubmodules { get; set; }
        public string DockerFile { get; set; }
        public string DockerImageName { get; set; }
        public string DockerLoad { get; set; } // Relative to the docker folder
        public string DockerContextDirectory { get; set; }
        public string DockerFetchPath { get; set; }
        public string LocalFolder { get; set; }

        public string GetNormalizedImageName()
        {
            if (!string.IsNullOrEmpty(DockerImageName))
            {
                if (DockerImageName.StartsWith("benchmarks_"))
                {
                    return DockerImageName;
                }

                return $"benchmarks_{DockerImageName}".ToLowerInvariant();
            }
            else
            {
                return $"benchmarks_{Path.GetFileNameWithoutExtension(DockerFile)}".ToLowerInvariant();
            }            
        }

        // When set, contains the location of the uploaded source code
        public Attachment SourceCode { get; set; }
    }
}
