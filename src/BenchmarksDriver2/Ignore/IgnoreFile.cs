using System.Collections.Generic;
using System.IO;

namespace BenchmarksDriver.Ignore
{
    public class IgnoreFile
    {
        private readonly string _gitIgnorePath;
        private readonly string _basePath;

        private IgnoreFile(string gitIgnorePath)
        {
            _gitIgnorePath = gitIgnorePath;
            _basePath = Path.GetDirectoryName(_gitIgnorePath).Replace("\\", "/") + "/";
        }

        public List<IgnoreRule> Rules { get; } = new List<IgnoreRule>();

        /// <summary>
        /// Parses a gitignore file.
        /// </summary>
        /// <param name="path">The path of the file to parse.</param>
        /// <returns>A list of <see cref="IgnoreRule"/> instances.</returns>
        public static IgnoreFile Parse(string path)
        {
            var ignoreFile = new IgnoreFile(path);

            // Ignore the .git folder by default
            ignoreFile.Rules.Add(IgnoreRule.Parse(".git/"));

            using (var stream = File.OpenText(path))
            {
                string rule = null;

                while (null != (rule = stream.ReadLine()))
                {
                    // A blank line matches no files, so it can serve as a separator for readability.
                    if (string.IsNullOrWhiteSpace(rule))
                    {
                        continue;
                    }

                    // A line starting with # serves as a comment. 
                    if (rule.StartsWith('#'))
                    {
                        continue;
                    }

                    var ignoreRule = IgnoreRule.Parse(rule);

                    if (ignoreRule != null)
                    {
                        ignoreFile.Rules.Add(ignoreRule);
                    }
                }
            }

            return ignoreFile;
        }

        /// <summary>
        /// Lists all the matching files.
        /// </summary>
        public IList<IGitFile> ListDirectory(string path)
        {
            var result = new List<IGitFile>();
            ListDirectory(path, result);

            return result;
        }

        private void ListDirectory(string path, List<IGitFile> accumulator)
        {
            foreach (var filename in Directory.EnumerateFiles(path))
            {
                var gitFile = new GitFile(filename);

                var ignore = false;

                foreach (var rule in Rules)
                {
                    if (rule.Match(_basePath, gitFile))
                    {
                        ignore = true;

                        if (rule.Negate)
                        {
                            ignore = false;
                        }
                    }
                }

                if (!ignore)
                {
                    accumulator.Add(gitFile);
                }
            }

            foreach (var directoryName in Directory.EnumerateDirectories(path))
            {
                var gitFile = new GitDirectory(directoryName);

                var ignore = false;

                foreach (var rule in Rules)
                {
                    if (rule.Match(_basePath, gitFile))
                    {
                        ignore = true;

                        if (rule.Negate)
                        {
                            ignore = false;
                        }
                    }
                }

                if (!ignore)
                {
                    ListDirectory(directoryName, accumulator);
                }
            }
        }
    }
}
