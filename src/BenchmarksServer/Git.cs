// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text.RegularExpressions;

namespace BenchmarkServer
{
    public static class Git
    {
        public static string Clone(string path, string repository, string branch = null)
        {
            var branchParam = string.IsNullOrEmpty(branch) ? string.Empty : $"-b {branch}";

            var result = RunGitCommand(path, $"clone {branchParam} {repository}");

            var match = Regex.Match(result.StandardError, @"'(.*)'");
            if (match.Success && match.Groups.Count == 2)
            {
                return match.Groups[1].Value;
            }
            else
            {
                throw new InvalidOperationException("Could not parse directory from 'git clone' standard error");
            }
        }

        private static ProcessResult RunGitCommand(string path, string command, bool throwOnError = true)
        {
            return ProcessUtil.Run("git", command, workingDirectory: path, throwOnError: throwOnError);
        }
    }
}
