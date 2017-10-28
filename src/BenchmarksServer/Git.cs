// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text.RegularExpressions;

namespace BenchmarkServer
{
    public static class Git
    {
        private const int CommandTimeout = 5000;

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

        public static void Checkout(string path, string branchOrCommit)
        {
            RunGitCommand(path, $"checkout {branchOrCommit}", retries: 5);
        }

        private static ProcessResult RunGitCommand(string path, string command, bool throwOnError = true, int retries = 0)
        {
            return RetryOnException(retries, () => ProcessUtil.Run("git", command, workingDirectory: path, throwOnError: throwOnError, timeout: CommandTimeout));
        }

        private static T RetryOnException<T>(int retries, Func<T> operation)
        {
            var attempts = 0;
            do
            {
                try
                {
                    attempts++;
                    return operation();
                }
                catch(Exception e)
                {
                    if (attempts == retries + 1)
                    {
                        throw;
                    }

                    Log.WriteLine($"Attempt {attempts} failed: {e.Message}");
                }
            } while (true);
        }
    }
}
