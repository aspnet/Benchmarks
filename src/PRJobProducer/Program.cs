﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.File;
using Microsoft.IdentityModel.Tokens;
using Octokit;

namespace PRJobProducer
{
    public class Program
    {
        private const string AppName = "pr-benchmarks";
        private const string Owner = "dotnet";
        private const string Repo = "AspNetCore";

        private const string ProcessedDirectoryName = "processed";

        private const string BenchmarkRequest = "@aspnet-hello benchmark";
        private const string StartingBencmarkComment = "Starting '{0}' pipelined plaintext benchmark with session ID '{1}'. This could take up to 30 minutes...";
        private const string CompletedBenchmarkCommentTemplate = "## Baseline\n\n```\n{0}\n```\n\n## PR\n\n```\n{1}\n```";

        private static readonly DateTime CommentCutoffDate = DateTime.Now.AddHours(-24);
        private static readonly TimeSpan BenchmarkTimeout = TimeSpan.FromMinutes(30);
        // GitHub will not allow a JWT timeout greater than 10 minutes. Trying to add exactly 10 minutes to the current system time is flaky.
        private static readonly TimeSpan GitHubJwtTimeout = TimeSpan.FromMinutes(5);

        private static IFileSystem JobFileSystem { get; set; }
        private static string BaseJobPath { get; set; }
        private static BuildInstructions BaseBuildInstructions { get; set; }

        public static int Main(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("env:", StringComparison.OrdinalIgnoreCase))
                {
                    args[i] = Environment.GetEnvironmentVariable(args[i].Substring(4));
                }
            }

            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");

            var baseJobPath = app.Option("-b|--base-job <PATH>", "The base job file path", CommandOptionType.SingleValue).IsRequired();

            var jobsPath = app.Option("-j|--jobs-path <PATH>", "The path where jobs are created", CommandOptionType.SingleValue);

            var azureStorageConnectionString = app.Option("-c|--azure-storage-connection-string <CONNECTIONSTRING>", "The Azure Storage connection string", CommandOptionType.SingleValue);
            var azureStorageFileShareName = app.Option("-f|--azure-storage-file-share-name <NAME>", "The Azure Storage file share name", CommandOptionType.SingleValue);

            var githubUser = app.Option("-u|--github-user <NAME>", "The GitHub user name for the bot", CommandOptionType.SingleValue);
            var githubUserToken = app.Option("-t|--github-user-token <TOKEN>", "The GitHub token for the bot", CommandOptionType.SingleValue);

            var githubAppId = app.Option("-a|--github-app-id <ID>", "The GitHub App ID for the bot", CommandOptionType.SingleValue);
            var githubAppKey = app.Option("-k|--github-app-key <KEY>", "The GitHub App pem", CommandOptionType.SingleValue);
            var githubAppInstallationId = app.Option("-i|--github-app-install-id <ID>", "The GitHub App installation ID for the repo. E.g. 'https://github.com/settings/installations/{Id}'", CommandOptionType.SingleValue);

            app.OnExecuteAsync(async cancellationToken =>
            {
                BaseJobPath = baseJobPath.Value();

                GitHubClient client;
                string botLoginName;

                if (githubUser.HasValue())
                {
                    if (!githubUserToken.HasValue())
                    {
                        Console.WriteLine("--github-user was provided with no --github-token.");
                        return -1;
                    }

                    botLoginName = githubUser.Value();
                    client = GetClientForUser(botLoginName, githubUserToken.Value());
                }
                else if (githubAppId.HasValue())
                {
                    if (!githubAppKey.HasValue())
                    {
                        Console.WriteLine("--github-app-id was provided with no --github-app-key.");
                        return -1;
                    }

                    if (!githubAppInstallationId.HasValue())
                    {
                        Console.WriteLine("--github-app-id was provided with no --github-app-install-id.");
                        return -1;
                    }

                    if (!long.TryParse(githubAppInstallationId.Value(), out long installId))
                    {
                        Console.WriteLine("--github-app-install-id is not a valid long.");
                    }

                    botLoginName = AppName + "[bot]";
                    client = await GetClientForApp(githubAppId.Value(), githubAppKey.Value(), installId);
                }
                else
                {
                    Console.WriteLine("Cannot authenticate with GitHub. Neither a --github-user nor a --github-app-id has been provided.");
                    return -1;
                }


                if (jobsPath.HasValue())
                {
                    JobFileSystem = new LocalFileSystem(jobsPath.Value());
                }
                else if (azureStorageConnectionString.HasValue())
                {
                    if (!azureStorageFileShareName.HasValue())
                    {
                        Console.WriteLine("--azure-storage-connection-string was provided with no --azure-storage-file-share.");
                        return -1;
                    }

                    var cloudDir = await GetCloudFileDirectory(azureStorageConnectionString.Value(), azureStorageFileShareName.Value());
                    JobFileSystem = new AzureStorageFileSystem(cloudDir);
                }
                else
                {
                    Console.WriteLine("Neither a --jobs-path nor an --azure-storage-connection-string has been provided.");
                    return -1;
                }

                await JobFileSystem.CreateDirectoryIfNotExists(ProcessedDirectoryName);
                BaseBuildInstructions = await GetBuildInstructions();

                Console.WriteLine($"Scanning for benchmark requests in {Owner}/{Repo}.");

                await foreach (var prBenchmarkRequest in GetPRsToBenchmark(client, botLoginName))
                {
                    var pr = prBenchmarkRequest.PullRequest;

                    try
                    {
                        var session = Guid.NewGuid().ToString("n");
                        var newJobFileName = $"{session}.{Path.GetFileName(BaseJobPath)}";
                        var startingCommentText = string.Format(StartingBencmarkComment, prBenchmarkRequest.ScenarioName, session);

                        Console.WriteLine($"Requesting {prBenchmarkRequest.ScenarioName} benchmark for PR #{pr.Number}.");
                        Console.WriteLine($"Benchmark starting comment: {startingCommentText}");

                        await client.Issue.Comment.Create(Owner, Repo, pr.Number, startingCommentText);

                        await RequestBenchmark(prBenchmarkRequest, newJobFileName);

                        Console.WriteLine($"Benchmark requested for PR #{pr.Number}. Waiting up to {BenchmarkTimeout} for results.");
                        var results = await WaitForBenchmarkResults(newJobFileName);

                        string FormatOutput(string stdout, string stderr)
                        {
                            return string.IsNullOrEmpty(stderr) ? stdout : $"stdout: {results.BaselineStdout}\nstderr: {results.BaselineStderr}";
                        }

                        var baselineOutput = FormatOutput(results.BaselineStdout, results.BaselineStderr);
                        var prOutput = FormatOutput(results.PullRequestStdout, results.PullRequestStderr);

                        var resultCommentText = string.Format(CompletedBenchmarkCommentTemplate, baselineOutput, prOutput);

                        Console.WriteLine($"Benchmark results received for PR #{pr.Number}. Posting results to {pr.Url}.");
                        Console.WriteLine($"Benchmark results comment: {resultCommentText}");

                        await client.Issue.Comment.Create(Owner, Repo, pr.Number, resultCommentText);
                    }
                    catch (Exception ex)
                    {
                        var errorCommentText = $"Failed to benchmark PR #{pr.Number}. Skipping... Details:\n```\n{ex}\n```";
                        Console.WriteLine($"Benchmark error comment: {errorCommentText}");
                        await client.Issue.Comment.Create(Owner, Repo, pr.Number, errorCommentText);
                    }
                }

                Console.WriteLine($"Done scanning for benchmark requests.");

                return 0;
            });

            return app.Execute(args);
        }

        private static async IAsyncEnumerable<PRBenchmarkRequest> GetPRsToBenchmark(GitHubClient client, string botLoginName)
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
                        var scenarioName = comment.Body.Substring(BenchmarkRequest.Length).Trim();

                        if (string.IsNullOrWhiteSpace(scenarioName))
                        {
                            scenarioName = "Default";
                        }

                        yield return new PRBenchmarkRequest
                        {
                            ScenarioName = scenarioName,
                            PullRequest = pr,
                        };
                    }
                    else if (comment.User.Login.Equals(botLoginName, StringComparison.OrdinalIgnoreCase))
                    {
                        // The bot has already commented with results for the most recent benchmark request.
                        break;
                    }
                }
            }
        }

        private static async Task<BuildInstructions> GetBuildInstructions()
        {
            using var processingJsonStream = File.OpenRead(BaseJobPath);
            using var jsonDocument = await JsonDocument.ParseAsync(processingJsonStream);

            if (!jsonDocument.RootElement.TryGetProperty(nameof(BuildInstructions), out var buildInstructionsElement))
            {
                throw new InvalidDataException($"Job file {Path.GetFileName(BaseJobPath)} doesn't include a top-level '{nameof(BuildInstructions)}' property.");
            }

            return JsonSerializer.Deserialize<BuildInstructions>(buildInstructionsElement.GetRawText());
        }

        private static async Task RequestBenchmark(PRBenchmarkRequest prBenchmarkRequest, string newJobFileName)
        {
            await using var baseJobStream = File.OpenRead(BaseJobPath);

            var jsonDictionary = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(baseJobStream);

            jsonDictionary[nameof(BuildInstructions)] = new BuildInstructions
            {
                BuildCommands = BaseBuildInstructions.BuildCommands,
                ExtraDriverArgs = BaseBuildInstructions.ExtraDriverArgs,
                PullRequestNumber = prBenchmarkRequest.PullRequest.Number,
                BaselineSHA = prBenchmarkRequest.PullRequest.Base.Sha,
                PullRequestSHA = prBenchmarkRequest.PullRequest.Head.Sha,
                ScenarioName = prBenchmarkRequest.ScenarioName,
            };

            using var newJobStream = new MemoryStream();
            await JsonSerializer.SerializeAsync(newJobStream, jsonDictionary, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            newJobStream.Position = 0;

            await JobFileSystem.WriteFile(newJobStream, newJobFileName);
        }

        private static async Task<BenchmarkResult> WaitForBenchmarkResults(string newJobFileName)
        {
            var startTicks = Environment.TickCount64;
            var expectedProcessedJobPath = Path.Combine(ProcessedDirectoryName, newJobFileName);

            if (!await WaitForCompleteJsonFile(expectedProcessedJobPath, BenchmarkTimeout))
            {
                throw new TimeoutException($"Benchmark results for job {newJobFileName} were not published to {ProcessedDirectoryName} within {BenchmarkTimeout}.");
            }

            Console.WriteLine($"Found '{newJobFileName}'");

            using var processedJsonStream = await JobFileSystem.ReadFile(expectedProcessedJobPath);
            using var jsonDocument = await JsonDocument.ParseAsync(processedJsonStream);

            if (!jsonDocument.RootElement.TryGetProperty(nameof(BenchmarkResult), out var benchmarkResultElement))
            {
                throw new InvalidDataException($"Processed benchmark job '{newJobFileName}' did not include a top-level '{nameof(BenchmarkResult)}' property.");
            }

            return JsonSerializer.Deserialize<BenchmarkResult>(benchmarkResultElement.GetRawText());
        }

        private static async Task<bool> WaitForCompleteJsonFile(string jsonFilePath, TimeSpan timeout)
        {
            var startTicks = Environment.TickCount64;

            while (!await JobFileSystem.FileExists(jsonFilePath))
            {
                if (Environment.TickCount64 - startTicks > timeout.TotalMilliseconds)
                {
                    return false;
                }

                await Task.Delay(1000);
            }

            // Wait up to 5 seconds for the Json file to be fully parsable.
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using var processedJsonStream = await JobFileSystem.ReadFile(jsonFilePath);
                    using var jsonDocument = await JsonDocument.ParseAsync(processedJsonStream);

                    return true;
                }
                catch (JsonException)
                {
                    if (i == 4)
                    {
                        throw;
                    }

                    await Task.Delay(1000);
                }
            }

            return false;
        }

        private static GitHubClient GetClientForUser(string userName, string token)
        {
            return new GitHubClient(new ProductHeaderValue(userName))
            {
                Credentials = new Credentials(token),
            };
        }

        private static async Task<GitHubClient> GetClientForApp(string appId, string key, long installId)
        {
            var creds = new SigningCredentials(GetRsaSecurityKeyFromPemKey(key), SecurityAlgorithms.RsaSha256);

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

        private static RsaSecurityKey GetRsaSecurityKeyFromPemKey(string keyText)
        {
            using var rsa = RSA.Create();

            var keyBytes = Convert.FromBase64String(keyText);

            rsa.ImportRSAPrivateKey(keyBytes, out _);

            return new RsaSecurityKey(rsa.ExportParameters(true));
        }

        private static async Task<CloudFileDirectory> GetCloudFileDirectory(string connectionString, string shareName)
        {
            var fileClient = CloudStorageAccount.Parse(connectionString).CreateCloudFileClient();
            var jobsShare = fileClient.GetShareReference(shareName);

            await jobsShare.CreateIfNotExistsAsync();

            return jobsShare.GetRootDirectoryReference();
        }

        // REVIEW: What's the best way to share these DTOs in this repo?
        private class BuildInstructions
        {
            public string[] BuildCommands { get; set; }
            public string ExtraDriverArgs { get; set; }

            public int PullRequestNumber { get; set; }
            public string BaselineSHA { get; set; }
            public string PullRequestSHA { get; set; }

            public string ScenarioName { get; set; }
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
