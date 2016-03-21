using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BenchmarkServer
{
    public class GitCommands
    {
        private string _repo;

        public GitCommands(string repo)
        {
            _repo = repo;
        }

        public string GetCurrentBranch()
        {
            var output = RunGitCommand("status -s -b");
            return Regex.Match(output, @"## (.*)\.\.\.").Groups[1].Value;
        }

        public void Fetch(string branch)
        {
            RunGitCommand($"fetch origin {branch}:{branch}");
        }

        public void Checkout(string branch)
        {
            RunGitCommand($"checkout {branch}");
        }

        public void Merge(string fromBranch)
        {
            RunGitCommand($"merge {fromBranch}");
        }

        public void DeleteBranch(string branch, bool throwOnError = true)
        {
            RunGitCommand($"branch -D {branch}", throwOnError);
        }

        private string RunGitCommand(string command, bool throwOnError = true)
        {
            return ProcessUtil.Run("git", command, workingDirectory: _repo, throwOnError: throwOnError);
        }
    }
}
