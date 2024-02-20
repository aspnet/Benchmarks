using System.CommandLine;

namespace RunTemplate;

internal static class GlobalOptions
{
    public static readonly Option<string> UrlsOption = new(["--urls"], "When specified, runs the app using the provided URLs")
    {
        AllowMultipleArgumentsPerToken = true
    };
}
