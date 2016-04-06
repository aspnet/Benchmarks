using System.Text.RegularExpressions;

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
