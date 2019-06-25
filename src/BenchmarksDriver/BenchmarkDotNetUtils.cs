using System;
using System.Linq;

namespace BenchmarksDriver
{
    internal static class BenchmarkDotNetUtils
    {
        internal static void WriteMarkdownResultTableToConsole(string markdownFileContent)
        {
            var lines = markdownFileContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach(var line in lines.Where(line => !line.StartsWith("```")))
            {
                // BDN uses "**" for getting the text bold when Benchmark uses Params, it looks bad in console
                // we need to trim every line to avoid some weird whitespace issues for Linux results
                Console.WriteLine(line.Replace("**", string.Empty).Trim());
            }
        }
    }
}
