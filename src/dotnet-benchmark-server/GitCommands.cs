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

        public string Fetch(int pullRequest)
        {
            var newBranch = $"pull/{pullRequest}";
            RunGitCommand($"fetch origin pull/{pullRequest}/head:{newBranch}");
            return newBranch;
        }

        public void Checkout(string branch)
        {
            RunGitCommand($"checkout {branch}");
        }

        public void Merge(string fromBranch)
        {
            RunGitCommand($"merge {fromBranch}");
        }

        public void DeleteBranch(string branch)
        {
            RunGitCommand($"branch -D {branch}");
        }

        private string RunGitCommand(string command)
        {
            Log.WriteLine($"git {command}");

            var process = new Process()
            {
                StartInfo =
                {
                    FileName = "git",
                    Arguments = command,
                    WorkingDirectory = _repo,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Git command {command} returned exit code {process.ExitCode}");
            }

            return process.StandardOutput.ReadToEnd();
        }
    }
}
