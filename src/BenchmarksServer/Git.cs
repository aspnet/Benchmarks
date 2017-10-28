// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BenchmarkServer
{
    public static class Git
    {
        /// <summary>
        /// Clones a git repository in the specified path, and caches it.
        /// </summary>
        /// <param name="path">The path where the repository should be cloned.</param>
        /// <param name="repository">The repository to clone.</param>
        /// <param name="branch">The branch to checkout.</param>
        /// <returns>The folder relative to <paramref name="path"/> where the repository was cloned.</returns>

        public static string CloneAndCache(string cachePath, string path, string repository, string branch = null)
        {
            // Clone the repository in the cache path if it's not already here
            var repositoryHash = Convert.ToBase64String(Encoding.UTF8.GetBytes(repository));
            var repositoryPath = Path.Combine(cachePath, repositoryHash);

            if (!Directory.Exists(repositoryPath))
            {
                Log.WriteLine($"Cloning and caching '{repository}' in '{cachePath}'");
                Clone(cachePath, repository, branch, repositoryHash);
            }
            else
            {
                Log.WriteLine($"Repository '{repository}' already cached locally, pulling only");
                Pull(repositoryPath);
            }

            // Clone the cached repository to the requested location, technically creating a copy of the cache repository
            return Clone(path, Path.Combine(cachePath, repositoryHash), branch);
        }

        /// <summary>
        /// Clones a git repository in the specified path.
        /// </summary>
        /// <param name="path">The path where the repository should be cloned.</param>
        /// <param name="repository">The repository to clone.</param>
        /// <param name="destination">The folder relative to <paramref name="path"/> where the repository is cloned.</param>
        /// <param name="branch">The branch to checkout.</param>
        /// <returns>The folder relative to <paramref name="path"/> where the repository was cloned.</returns>
        public static string Clone(string path, string repository, string branch = null, string destination = null)
        {
            var branchParam = string.IsNullOrEmpty(branch) ? string.Empty : $"-b {branch}";

            var result = RunGitCommand(path, $"clone {branchParam} {repository} {destination}");

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
            RunGitCommand(path, $"checkout {branchOrCommit}");
        }

        public static void Pull(string path)
        {
            RunGitCommand(path, $"pull");
        }

        private static ProcessResult RunGitCommand(string path, string command, bool throwOnError = true)
        {
            return ProcessUtil.Run("git", command, workingDirectory: path, throwOnError: throwOnError);
        }
    }
}
