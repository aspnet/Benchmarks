// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.IdentityModel.Tokens;
using Octokit;

namespace PRJobProducer
{
    public class Program
    {
        private const string AppName = "pr-benchmarks";
        private const string Owner = "aspnet";
        private const string Repo = "AspNetCore";

        private const string BenchmarkRequest = "@aspnet-hello benchmark";
        private const string CommentTemplate = "## Baseline\n\n```\n{0}\n```\n\n## PR\n\n```\n{1}\n```";

        private static readonly DateTime CommentCutoffDate = DateTime.Now.AddHours(-24);
        private static readonly TimeSpan BenchmarkTimeout = TimeSpan.FromMinutes(30);
        // GitHub will not allow a JWT timeout greater than 10 minutes. Trying to add exactly 10 minutes to the current system time is flaky.
        private static readonly TimeSpan GitHubJwtTimeout = TimeSpan.FromMinutes(5);

        private static string JobsPath { get; set; }
        private static string BaseJobPath { get; set; }
        private static string[] BuildCommands { get; set; }

        private static string ProcessedPath => Path.Combine(JobsPath, "processed");

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");

            var jobsPath = app.Option("-j|--jobs-path <PATH>", "The path where jobs are created", CommandOptionType.SingleValue).IsRequired();
            var baseJobPath = app.Option("-b|--base-job <PATH>", "The base job file path", CommandOptionType.SingleValue).IsRequired();

            var optionBotUser = app.Option("-u|--user <NAME>", "The GitHub user name for the bot", CommandOptionType.SingleValue);
            var optionBotToken = app.Option("-t|--token <TOKEN>", "The GitHub token for the bot", CommandOptionType.SingleValue);

            var optionAppId = app.Option("-a|--app-id <ID>", "The GitHub App ID for the bot", CommandOptionType.SingleValue);
            var optionAppKeyPath = app.Option("-k|--key-file <PATH>", "The GitHub App pem file path", CommandOptionType.SingleValue);
            var optionAppInstallationId = app.Option("-i|--install-id <ID>", "The GitHub App installation ID for the repo. E.g. 'https://github.com/settings/installations/{Id}'", CommandOptionType.SingleValue);

            app.OnExecuteAsync(async cancellationToken =>
            {
                JobsPath = jobsPath.Value();
                BaseJobPath = baseJobPath.Value();

                GitHubClient client;
                string botLoginName;

                if (!Directory.Exists(JobsPath))
                {
                    Console.WriteLine($"The path doesn't exist: '{JobsPath}'");
                    return -1;
                }

                BuildCommands = await GetBuildCommands();

                if (optionBotUser.HasValue())
                {
                    if (!optionBotToken.HasValue())
                    {
                        Console.WriteLine("--user was provided with no --token.");
                        return -1;
                    }

                    botLoginName = optionBotUser.Value();
                    client = GetClientForUser(botLoginName, optionBotToken.Value());
                }
                else if (optionAppId.HasValue())
                {
                    if (!optionAppKeyPath.HasValue())
                    {
                        Console.WriteLine("--app-id was provided with no --key-file.");
                        return -1;
                    }

                    if (!optionAppInstallationId.HasValue())
                    {
                        Console.WriteLine("--app-id was provided with no --install-id.");
                        return -1;
                    }

                    if (!long.TryParse(optionAppInstallationId.Value(), out long installId))
                    {
                        Console.WriteLine("--install-id is not a valid long.");
                    }

                    botLoginName = AppName + "[bot]";
                    client = await GetClientForApp(optionAppId.Value(), optionAppKeyPath.Value(), installId);
                }
                else
                {
                    Console.WriteLine("Cannot authenticate with GitHub. Neither a --user nor an --app-id has been provided.");
                    return -1;
                }

                await foreach (var pr in GetPRsToBenchmark(client, botLoginName))
                {
                    try
                    {
                        var session = Guid.NewGuid().ToString("n");

                        Console.WriteLine($"Requesting benchmark for PR #{pr.Number} with session id:'{session}'");
                        await RequestBenchmark(pr, session);

                        Console.WriteLine($"Benchmark requested for PR #{pr.Number}. Waiting up to {BenchmarkTimeout} for results.");
                        var results = await WaitForBenchmarkResults(session);

                        Console.WriteLine($"Benchmark results received for PR #{pr.Number}. Posting results to {pr.Url}.");

                        string FormatOutput(string stdout, string stderr)
                        {
                            return string.IsNullOrEmpty(stderr) ? stdout : $"stdout: {results.BaselineStdout}\nstderr: {results.BaselineStderr}";
                        }

                        var baselineOutput = FormatOutput(results.BaselineStdout, results.BaselineStderr);
                        var prOutput = FormatOutput(results.PullRequestStdout, results.PullRequestStderr);
                        await client.Issue.Comment.Create(Owner, Repo, pr.Number, string.Format(CommentTemplate, baselineOutput, prOutput));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to benchmark PR #{pr.Number}. Skipping... Details: {ex}");
                    }
                }

                return 0;
            });

            return app.Execute(args);
        }

        private static async IAsyncEnumerable<PullRequest> GetPRsToBenchmark(GitHubClient client, string botLoginName)
        {
            var prRequest = new PullRequestRequest()
            {
                State = ItemStateFilter.Open,
                SortDirection = SortDirection.Descending,
                SortProperty = PullRequestSort.Updated,
            };

            var prs = await client.PullRequest.GetAllForRepository(Owner, Repo, prRequest);

            foreach (var pr in prs)
            {
                if (pr.UpdatedAt < CommentCutoffDate)
                {
                    break;
                }

                var comments = await client.Issue.Comment.GetAllForIssue(Owner, Repo, pr.Number);

                for (var i = comments.Count - 1; i >= 0; i--)
                {
                    var comment = comments[i];

                    if (comment.CreatedAt < CommentCutoffDate)
                    {
                        break;
                    }

                    if (comment.Body.StartsWith(BenchmarkRequest) && await client.Organization.Member.CheckMember(Owner, comment.User.Login))
                    {
                        yield return pr;
                    }
                    else if (comment.User.Login.Equals(botLoginName, StringComparison.OrdinalIgnoreCase))
                    {
                        // The bot has already commented with results for the most recent benchmark request.
                        break;
                    }
                }
            }
        }

        private static async Task<string[]> GetBuildCommands()
        {
            using var processingJsonStream = File.OpenRead(BaseJobPath);
            using var jsonDocument = await JsonDocument.ParseAsync(processingJsonStream);
            BuildInstructions buildInstructions = null;

            foreach (var element in jsonDocument.RootElement.EnumerateObject())
            {
                if (element.NameEquals("BuildInstructions"))
                {
                    buildInstructions = JsonSerializer.Deserialize<BuildInstructions>(element.Value.GetRawText());
                    break;
                }
            }

            if (buildInstructions is null)
            {
                throw new InvalidDataException("Job file doesn't include a valid 'BuildInstructions' property");
            }

            return buildInstructions.BuildCommands;
        }

        private static async Task RequestBenchmark(PullRequest pr, string session)
        {
            var baseFile = new FileInfo(BaseJobPath);
            var newJobPath = Path.Combine(Path.GetTempPath(), $"{session}.{baseFile.Name}");
            var newJobFile = new FileInfo(newJobPath);

            baseFile.CopyTo(newJobPath);

            try
            {
                using (var newJobStream = File.Open(newJobPath, System.IO.FileMode.Open))
                {
                    var jsonDictionary = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(newJobStream);

                    jsonDictionary["BuildInstructions"] = new BuildInstructions
                    {
                        BuildCommands = BuildCommands,
                        BaselineSHA = pr.Base.Sha,
                        PullRequestSHA = pr.Head.Sha,
                    };

                    // Clear file and reset position to 0
                    newJobStream.SetLength(0);
                    await JsonSerializer.SerializeAsync(newJobStream, jsonDictionary, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    });
                }

                newJobFile.MoveTo(Path.Combine(JobsPath, newJobFile.Name));
            }
            catch
            {
                newJobFile.Delete();
                throw;
            }
        }

        private static async Task<BenchmarkResult> WaitForBenchmarkResults(string session)
        {
            var directory = new DirectoryInfo(ProcessedPath);
            var startTicks = Environment.TickCount64;

            while (true)
            {
                var processedFile = directory
                    .GetFiles()
                    .Where(f => f.Name.StartsWith(session, StringComparison.Ordinal))
                    .OrderByDescending(f => f.LastWriteTime)
                    .SingleOrDefault();

                // If no file was found, wait some time
                if (processedFile is null)
                {
                    if (Environment.TickCount64 - startTicks > BenchmarkTimeout.TotalMilliseconds)
                    {
                        throw new TimeoutException($"Benchmark results for session {session} were not published to {ProcessedPath} within {BenchmarkTimeout}.");
                    }

                    await Task.Delay(1000);
                    continue;
                }

                Console.WriteLine($"Found '{processedFile.Name}'");

                using var processedJsonStream = File.OpenRead(processedFile.FullName);
                using var jsonDocument = await JsonDocument.ParseAsync(processedJsonStream);

                foreach (var element in jsonDocument.RootElement.EnumerateObject())
                {
                    if (element.NameEquals("BenchmarkResult"))
                    {
                        return JsonSerializer.Deserialize<BenchmarkResult>(element.Value.GetRawText());
                    }
                }
            }
        }

        private static GitHubClient GetClientForUser(string userName, string token)
        {
            return new GitHubClient(new ProductHeaderValue(userName))
            {
                Credentials = new Credentials(token),
            };
        }

        private static async Task<GitHubClient> GetClientForApp(string appId, string keyPath, long installId)
        {
            var creds = new SigningCredentials(GetRsaSecurityKeyFromPemFile(keyPath), SecurityAlgorithms.RsaSha256);

            var jwtToken = new JwtSecurityToken(
                new JwtHeader(creds),
                new JwtPayload(
                    issuer: appId,
                    issuedAt: DateTime.Now,
                    expires: DateTime.Now.Add(GitHubJwtTimeout),
                    audience: null,
                    claims: null,
                    notBefore: null));

            var jwtTokenString = new JwtSecurityTokenHandler().WriteToken(jwtToken);
            var initClient = new GitHubClient(new ProductHeaderValue(AppName))
            {
                Credentials = new Credentials(jwtTokenString, AuthenticationType.Bearer),
            };

            var installationToken = await initClient.GitHubApps.CreateInstallationToken(installId);
            return new GitHubClient(new ProductHeaderValue(AppName))
            {
                Credentials = new Credentials(installationToken.Token, AuthenticationType.Bearer),
            };
        }

        private static RsaSecurityKey GetRsaSecurityKeyFromPemFile(string pemPath)
        {
            const string pemStart = "-----BEGIN RSA PRIVATE KEY-----\n";
            const string pemEnd = "\n-----END RSA PRIVATE KEY-----\n";

            using var rsa = RSA.Create();

            var keyText = File.ReadAllText(pemPath);

            if (!keyText.StartsWith(pemStart, StringComparison.Ordinal) ||
                !keyText.EndsWith(pemEnd, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The --key-file is not in the expected pem format.");
            }

            var base64string = keyText.Substring(pemStart.Length, keyText.Length - pemStart.Length - pemEnd.Length);

            var keyBytes = Convert.FromBase64String(base64string);

            rsa.ImportRSAPrivateKey(keyBytes, out _);

            return new RsaSecurityKey(rsa.ExportParameters(true));
        }

        // REVIEW: What's the best way to share these DTOs in this repo?
        private class BuildInstructions
        {
            public string[] BuildCommands { get; set; }

            public string BaselineSHA { get; set; }
            public string PullRequestSHA { get; set; }
        }

        private class BenchmarkResult
        {
            public bool Success { get; set; }
            public string BaselineStdout { get; set; }
            public string BaselineStderr { get; set; }
            public string PullRequestStdout { get; set; }
            public string PullRequestStderr { get; set; }
        }
    }
}
