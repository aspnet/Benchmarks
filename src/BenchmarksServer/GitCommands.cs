// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace BenchmarkServer
{
    public static class Git
    {
        public static void Clone(string path, string repoUrl, string branch, string directory)
        {
            RunGitCommand(path, $"clone -b {branch} {repoUrl} {directory}");
        }

        private static string RunGitCommand(string path, string command, bool throwOnError = true)
        {
            return ProcessUtil.Run("git", command, workingDirectory: path, throwOnError: throwOnError);
        }
    }
}
