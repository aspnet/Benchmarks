using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Mono.Cecil;
using Octokit;

namespace BenchmarksBot
{
    class Program
    {
        private const string AppName = "pr-benchmarks";

        static readonly TimeSpan RecentIssuesTimeSpan = TimeSpan.FromDays(8);
        static readonly TimeSpan GitHubJwtTimeout = TimeSpan.FromMinutes(5);

        static readonly string _aspnetCoreUrlFormat = "https://dotnetcli.azureedge.net/dotnet/aspnetcore/Runtime/{0}/aspnetcore-runtime-{0}-win-x64.zip";
        static readonly string _dotnetCoreUrlFormat = "https://dotnetcli.azureedge.net/dotnet/Runtime/{0}/dotnet-runtime-{0}-win-x64.zip";

        static readonly HttpClient _httpClient = new HttpClient();

        static ProductHeaderValue _productHeaderValue = new ProductHeaderValue("BenchmarksBot");
        static long _repositoryId;
        static string _accessToken;
        static string _username;
        static string _githubAppId;
        static string _githubAppKey;
        static string _githubAppInstallationId;

        static Credentials _credentials;

        static string _connectionString;
        static HashSet<string> _ignoredScenarios;
        static string _tableName;

        static IReadOnlyList<Issue> _recentIssues;

        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix: "BENCHMARKSBOT_")
                .AddCommandLine(args)
                .Build();

            await LoadSettings(config);

            // Regresions

            Console.WriteLine("Looking for regressions...");

            var regressions = await FindRegression();

            Console.WriteLine("Excluding the ones already reported...");

            var newRegressions = await RemoveReportedRegressions(regressions, false, r => r.DateTimeUtc.ToString("u"));

            await PopulateHashes(newRegressions);

            if (newRegressions.Any())
            {
                Console.WriteLine("Reporting new regressions...");

                await CreateRegressionIssue(newRegressions);
            }
            else
            {
                Console.WriteLine("No new regressions where found.");
            }

            // Not running

            Console.WriteLine("Looking for scnearios that are not running...");

            var notRunning = await FindNotRunning();

            Console.WriteLine("Excluding the ones already reported...");

            // If the LastRun date doesn't match either it's because it was fixed then broke again since last reported issue
            notRunning = await RemoveReportedRegressions(notRunning, true, r => $"| {r.Scenario} | {r.OperatingSystem}, {r.Hardware}, {r.Scheme}, {r.WebHost} | {r.DateTimeUtc.ToString("u")} |");

            if (notRunning.Any())
            {
                Console.WriteLine("Reporting new scenarios...");

                await CreateNotRunningIssue(notRunning);
            }
            else
            {
                Console.WriteLine("All scenarios are running correctly.");
            }

            // Bad responses

            Console.WriteLine("Looking for scenarios that have errors...");

            var badResponses = await FindErrors();

            Console.WriteLine("Excluding the ones already reported...");

            badResponses = await RemoveReportedRegressions(badResponses, true, r => $"| {r.Scenario} | {r.OperatingSystem}, {r.Hardware}, {r.Scheme}, {r.WebHost} |");

            if (badResponses.Any())
            {
                Console.WriteLine("Reporting new scenarios...");

                await CreateErrorsIssue(badResponses);
            }
            else
            {
                Console.WriteLine("All scenarios are running correctly.");
            }
        }

        private static async Task LoadSettings(IConfiguration config)
        {
            // Tip: The repository id can be found using this endpoint: https://api.github.com/repos/dotnet/aspnetcore

            long.TryParse(config["RepositoryId"], out _repositoryId);
            _accessToken = config["AccessToken"];
            _username = config["Username"];
            _githubAppKey = config["GithHubAppKey"];
            _githubAppId = config["GitHubAppId"];
            _githubAppInstallationId = config["GitHubInstallationId"];

            _connectionString = config["ConnectionString"];
            _ignoredScenarios = new HashSet<string>();
            _tableName = config["Table"];

            if (_repositoryId == 0)
            {
                throw new ArgumentException("RepositoryId argument is missing or invalid");
            }

            if (String.IsNullOrEmpty(_accessToken) && String.IsNullOrEmpty(_githubAppKey))
            {
                throw new ArgumentException("AccessToken or GitHubAppKey is required");
            }
            else if (!String.IsNullOrEmpty(_githubAppKey))
            {
                if(String.IsNullOrEmpty(_githubAppId))
                {
                    throw new ArgumentException("GitHubAppId argument is missing");
                }

                if (String.IsNullOrEmpty(_githubAppInstallationId))
                {
                    throw new ArgumentException("GitHubInstallationId argument is missing");
                }

                if (!long.TryParse(_githubAppInstallationId, out var installationId))
                {
                    throw new ArgumentException("GitHubInstallationId should be a number or is invalid");
                }

                _credentials = await GetCredentialsForApp(_githubAppId, _githubAppKey, installationId);
            }
            else
            {
                if (String.IsNullOrEmpty(_username))
                {
                    throw new ArgumentException("BotUsername argument is missing");
                }

                _credentials = GetCredentialsForUser(_username, _accessToken);
            }
            if (String.IsNullOrEmpty(_connectionString))
            {
                throw new ArgumentException("ConnectionString argument is missing");
            }

            var ignore = config["Ignore"];

            if (!String.IsNullOrEmpty(ignore))
            {
                _ignoredScenarios = new HashSet<string>(ignore.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            }
        }

        private static async Task CreateRegressionIssue(IEnumerable<Regression> regressions)
        {
            // Remove scenarions that shouldn't be reported
            regressions = regressions.Where(x => Tags.ReportRegressions(x.Scenario)).ToArray();

            if (regressions == null || !regressions.Any())
            {
                return;
            }

            var client = new GitHubClient(_productHeaderValue);
            client.Credentials = _credentials;

            var body = new StringBuilder();
            body.Append("A performance regression has been detected for the following scenarios:");

            foreach (var r in regressions.OrderBy(x => x.Scenario).ThenBy(x => x.DateTimeUtc))
            {
                body.AppendLine();
                body.AppendLine();
                body.AppendLine("| Scenario | Environment | Date | Old RPS | New RPS | Change | Deviation |");
                body.AppendLine("| -------- | ----------- | ---- | ------- | ------- | ------ | --------- |");

                var prevRPS = r.Values.Skip(2).First();
                var rps = r.Values.Last();
                var change = Math.Round((double)(rps - prevRPS) / prevRPS * 100, 2);
                var deviation = Math.Round((double)(rps - prevRPS) / r.Stdev, 2);

                body.AppendLine($"| {r.Scenario} | {r.OperatingSystem}, {r.Scheme}, {r.WebHost} | {r.DateTimeUtc.ToString("u")} | {prevRPS.ToString("n0")} | {rps.ToString("n0")} | {change} % | {deviation} σ |");


                body.AppendLine();
                body.AppendLine("Before versions:");

                body.AppendLine($"ASP.NET Core __{r.PreviousAspNetCoreVersion}__");
                body.AppendLine($".NET Core __{r.PreviousDotnetCoreVersion}__");

                body.AppendLine();
                body.AppendLine("After versions:");

                body.AppendLine($"ASP.NET Core __{r.CurrentAspNetCoreVersion}__");
                body.AppendLine($".NET Core __{r.CurrentDotnetCoreVersion}__");

                var aspNetChanged = r.PreviousAspNetCoreVersion != r.CurrentAspNetCoreVersion;
                var runtimeChanged = r.PreviousDotnetCoreVersion != r.CurrentDotnetCoreVersion;

                if (aspNetChanged || runtimeChanged)
                {
                    body.AppendLine();
                    body.AppendLine("Commits:");

                    if (aspNetChanged)
                    {
                        if (r.AspNetCoreHashes != null && r.AspNetCoreHashes.Length == 2 && r.AspNetCoreHashes[0] != null && r.AspNetCoreHashes[1] != null)
                        {
                            body.AppendLine();
                            body.AppendLine("__ASP.NET Core__");
                            body.AppendLine($"https://github.com/dotnet/aspnetcore/compare/{r.AspNetCoreHashes[0]}...{r.AspNetCoreHashes[1]}");
                        }
                    }

                    if (runtimeChanged)
                    {
                        if (r.DotnetCoreHashes != null && r.DotnetCoreHashes.Length == 2 && r.DotnetCoreHashes[0] != null && r.DotnetCoreHashes[1] != null)
                        {
                            body.AppendLine();
                            body.AppendLine("__.NET Core__");
                            body.AppendLine($"https://github.com/dotnet/runtime/compare/{r.DotnetCoreHashes[0]}...{r.DotnetCoreHashes[1]}");
                        }
                    }
                }
            }


            body
                .AppendLine()
                .AppendLine("[Logs](https://dev.azure.com/dnceng/internal/_build?definitionId=825&_a=summary)")
                ;

            var title = "Performance regression: " + String.Join(", ", regressions.Select(x => x.Scenario).Take(5));

            if (regressions.Count() > 5)
            {
                title += " ...";
            }

            var createIssue = new NewIssue(title)
            {
                Body = body.ToString()
            };

            createIssue.Labels.Add("Perf");
            createIssue.Labels.Add("perf-regression");

            AssignTags(createIssue, regressions.Select(x => x.Scenario));

            Console.WriteLine(createIssue.Body);
            Console.WriteLine(String.Join(", ", createIssue.Labels));

            var issue = await client.Issue.Create(_repositoryId, createIssue);
        }

        private static async Task<IEnumerable<Regression>> FindRegression()
        {
            var regressions = new List<Regression>();

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(Queries.Regressions.Replace("@table", _tableName), connection))
                {
                    await connection.OpenAsync();

                    var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        regressions.Add(new Regression
                        {
                            Session = Convert.ToString(reader["Session"]),
                            Scenario = Convert.ToString(reader["Scenario"]),
                            Hardware = Convert.ToString(reader["Hardware"]),
                            OperatingSystem = Convert.ToString(reader["OperatingSystem"]),
                            Scheme = Convert.ToString(reader["Scheme"]),
                            WebHost = Convert.ToString(reader["WebHost"]),
                            DateTimeUtc = (DateTimeOffset)(reader["DateTime"]),
                            Values = new[] {
                                Convert.ToInt32(reader["PreviousRPS5"]),
                                Convert.ToInt32(reader["PreviousRPS4"]),
                                Convert.ToInt32(reader["PreviousRPS3"]),
                                Convert.ToInt32(reader["PreviousRPS2"]),
                                Convert.ToInt32(reader["PreviousRPS1"]),
                                Convert.ToInt32(reader["RequestsPerSecond"])
                            },
                            Stdev = (double)reader["STDEV"],
                            PreviousAspNetCoreVersion = Convert.ToString(reader["PreviousAspNetCoreVersion"]),
                            CurrentAspNetCoreVersion = Convert.ToString(reader["CurrentAspNetCoreVersion"]),
                            PreviousDotnetCoreVersion = Convert.ToString(reader["PreviousRuntimeVersion"]),
                            CurrentDotnetCoreVersion = Convert.ToString(reader["CurrentRuntimeVersion"])
                        });
                    }
                }
            }

            return regressions;
        }

        private static async Task<IEnumerable<Regression>> FindNotRunning()
        {
            var regressions = new List<Regression>();

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(Queries.NotRunning.Replace("@table", _tableName), connection))
                {
                    await connection.OpenAsync();

                    var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        regressions.Add(new Regression
                        {
                            Scenario = Convert.ToString(reader["Scenario"]),
                            Hardware = Convert.ToString(reader["Hardware"]),
                            OperatingSystem = Convert.ToString(reader["OperatingSystem"]),
                            Scheme = Convert.ToString(reader["Scheme"]),
                            WebHost = Convert.ToString(reader["WebHost"]),
                            DateTimeUtc = (DateTimeOffset)(reader["LastDateTime"]),
                        });
                    }
                }
            }

            return regressions;
        }

        private static async Task CreateNotRunningIssue(IEnumerable<Regression> regressions)
        {
            if (regressions == null || !regressions.Any())
            {
                return;
            }

            var client = new GitHubClient(_productHeaderValue);
            client.Credentials = _credentials;

            var body = new StringBuilder();
            body.Append("Some scenarios have stopped running:");

            body.AppendLine();
            body.AppendLine();
            body.AppendLine("| Scenario | Environment | Last Run |");
            body.AppendLine("| -------- | ----------- | -------- |");

            foreach (var r in regressions.OrderBy(x => x.Scenario).ThenBy(x => x.DateTimeUtc))
            {
                body.AppendLine($"| {r.Scenario} | {r.OperatingSystem}, {r.Hardware}, {r.Scheme}, {r.WebHost} | {r.DateTimeUtc.ToString("u")} |");
            }

            body
                .AppendLine()
                .AppendLine("[Logs](https://dev.azure.com/dnceng/internal/_build?definitionId=825&_a=summary)")
                ;

            var title = "Scenarios are not running: " + String.Join(", ", regressions.Select(x => x.Scenario).Take(5));

            if (regressions.Count() > 5)
            {
                title += " ...";
            }

            var createIssue = new NewIssue(title)
            {
                Body = body.ToString()
            };

            createIssue.Labels.Add("Perf");
            createIssue.Labels.Add("perf-not-running");

            AssignTags(createIssue, regressions.Select(x => x.Scenario));

            Console.WriteLine(createIssue.Body);
            Console.WriteLine(String.Join(", ", createIssue.Labels));

            var issue = await client.Issue.Create(_repositoryId, createIssue);
        }

        private static async Task<IEnumerable<Regression>> FindErrors()
        {
            var regressions = new List<Regression>();

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(Queries.Error.Replace("@table", _tableName), connection))
                {
                    await connection.OpenAsync();

                    var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        regressions.Add(new Regression
                        {
                            Scenario = Convert.ToString(reader["Scenario"]),
                            Hardware = Convert.ToString(reader["Hardware"]),
                            OperatingSystem = Convert.ToString(reader["OperatingSystem"]),
                            Scheme = Convert.ToString(reader["Scheme"]),
                            WebHost = Convert.ToString(reader["WebHost"]),
                            DateTimeUtc = (DateTimeOffset)(reader["LastDateTime"]),
                            Errors = Convert.ToInt32(reader["Errors"]),
                        });
                    }
                }
            }

            return regressions;
        }

        private static async Task CreateErrorsIssue(IEnumerable<Regression> regressions)
        {
            if (regressions == null || !regressions.Any())
            {
                return;
            }

            var client = new GitHubClient(_productHeaderValue);
            client.Credentials = _credentials;

            var body = new StringBuilder();
            body.Append("Some scenarios return errors:");

            body.AppendLine();
            body.AppendLine();
            body.AppendLine("| Scenario | Environment | Last Run | Errors |");
            body.AppendLine("| -------- | ----------- | -------- | ------ |");

            foreach (var r in regressions.OrderBy(x => x.Scenario).ThenBy(x => x.DateTimeUtc))
            {
                body.AppendLine($"| {r.Scenario} | {r.OperatingSystem}, {r.Hardware}, {r.Scheme}, {r.WebHost} | {r.DateTimeUtc.ToString("u")} | {r.Errors} |");
            }

            body
                .AppendLine()
                .AppendLine("[Logs](https://dev.azure.com/dnceng/internal/_build?definitionId=825&_a=summary)")
                ;

            var title = "Bad responses: " + String.Join(", ", regressions.Select(x => x.Scenario).Take(5));

            if (regressions.Count() > 5)
            {
                title += " ...";
            }

            var createIssue = new NewIssue(title)
            {
                Body = body.ToString()
            };

            createIssue.Labels.Add("Perf");
            createIssue.Labels.Add("perf-bad-response");

            AssignTags(createIssue, regressions.Select(x => x.Scenario));

            Console.WriteLine(createIssue.Body);
            Console.WriteLine(String.Join(", ", createIssue.Labels));

            var issue = await client.Issue.Create(_repositoryId, createIssue);
        }

        /// <summary>
        /// Returns the issues from the past <see cref="RecentIssuesTimeSpan"/>
        /// </summary>
        private static async Task<IReadOnlyList<Issue>> GetRecentIssues()
        {
            if (_recentIssues != null)
            {
                return _recentIssues;
            }

            var client = new GitHubClient(_productHeaderValue);
            client.Credentials = _credentials;

            var recently = new RepositoryIssueRequest
            {
                Filter = IssueFilter.Created,
                State = ItemStateFilter.All,
                Since = DateTimeOffset.Now.Subtract(RecentIssuesTimeSpan)
            };

            var issues = await client.Issue.GetAllForRepository(_repositoryId, recently);

            return _recentIssues = issues;
        }

        /// <summary>
        /// Filters out scenarios that have already been reported.
        /// </summary>
        /// <param name="regressions">The regressions to find in existing issues.</param>
        /// <param name="ignoreClosedIssues">True to report a scenario even if it's in an existing issue that is closed.</param>
        /// <param name="textToFind">The formatted text to find in an issue.</param>
        /// <returns></returns>
        private static async Task<IEnumerable<Regression>> RemoveReportedRegressions(IEnumerable<Regression> regressions, bool ignoreClosedIssues, Func<Regression, string> textToFind)
        {
            if (!regressions.Any())
            {
                return regressions;
            }

            var issues = await GetRecentIssues();

            // The list of regressions that will actually be reported
            var filtered = new List<Regression>();

            // Look for the same timestamp in all reported issues
            foreach (var r in regressions)
            {
                // When filter is false the regression is kept
                var filter = false;

                foreach (var issue in issues)
                {
                    // If ignoreClosedIssues is true, we don't remove scenarios from closed issues.
                    // It means that if an issue is already reported in a closed issue, it won't be filtered, hence it will be reported,
                    // and closing an issue allows the bot to repeat itself and reopen the scenario

                    if (ignoreClosedIssues && issue.State == ItemState.Closed)
                    {
                        continue;
                    }

                    if (issue.Body != null && issue.Body.Contains(textToFind(r)))
                    {
                        filter = true;
                        break;
                    }

                    if (_ignoredScenarios.Contains(r.Scenario))
                    {
                        filter = true;
                        break;
                    }
                }

                if (!filter)
                {
                    filtered.Add(r);
                }
            }

            return filtered;
        }

        private static async Task PopulateHashes(IEnumerable<Regression> regressions)
        {
            foreach (var r in regressions)
            {
                if (r.PreviousAspNetCoreVersion != r.CurrentAspNetCoreVersion)
                {
                    r.AspNetCoreHashes = new[]
                    {
                        await GetAspNetCommitHash(r.PreviousDotnetCoreVersion),
                        await GetAspNetCommitHash(r.CurrentDotnetCoreVersion),
                    };
                }

                if (r.PreviousDotnetCoreVersion != r.CurrentDotnetCoreVersion)
                {
                    r.DotnetCoreHashes = new[]
                    {
                        await GetDotnetCommitHash(r.PreviousDotnetCoreVersion),
                        await GetDotnetCommitHash(r.CurrentDotnetCoreVersion),
                    };
                }
            }
        }

        private static async Task<bool> DownloadFileAsync(string url, string outputPath, int maxRetries = 3, int timeout = 5)
        {
            for (var i = 0; i < maxRetries; ++i)
            {
                try
                {
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
                    var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);
                    response.EnsureSuccessStatusCode();

                    // This probably won't use async IO on windows since the stream
                    // needs to created with the right flags
                    using (var stream = File.Create(outputPath))
                    {
                        // Copy the response stream directly to the file stream
                        await response.Content.CopyToAsync(stream);
                    }

                    return true;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while downloading {url}:");
                    Console.WriteLine(e);
                }
            }

            return false;
        }

        public static Task<string> GetDotnetCommitHash(string version)
        {
            return GetRuntimeAssemblyCommitHash(_dotnetCoreUrlFormat, version, "Microsoft.NETCore.App", "System.Collections.dll", ExtractDotnetCoreHash);
        }

        public static Task<string> GetAspNetCommitHash(string version)
        {
            return GetRuntimeAssemblyCommitHash(_aspnetCoreUrlFormat, version, "Microsoft.AspNetCore.App", "Microsoft.AspNetCore.dll", ExtractAspNetCoreHash);
        }

        private static async Task<string> GetRuntimeAssemblyCommitHash(string runtimeUrlFormat, string runtimVersion, string runtimeName, string assemblyName, Func<AssemblyDefinition, string> extractor)
        {
            var packagePath = Path.GetTempFileName();

            try
            {
                // Download the runtime

                var netCoreAppUrl = String.Format(runtimeUrlFormat, runtimVersion);
                if (!await DownloadFileAsync(netCoreAppUrl, packagePath))
                {
                    return null;
                }

                using (var archive = ZipFile.OpenRead(packagePath))
                {
                    var versionAssemblyPath = Path.GetTempFileName();

                    try
                    {
                        var entry = archive.GetEntry($@"shared\{runtimeName}\{runtimVersion}\{assemblyName}")
                            ?? archive.GetEntry($@"shared/{runtimeName}/{runtimVersion}/{assemblyName}");

                        if (entry == null)
                        {
                            throw new InvalidDataException($"'{netCoreAppUrl}' doesn't contain 'shared/{{runtimeName}}/{runtimVersion}/{assemblyName}'");
                        }

                        entry.ExtractToFile(versionAssemblyPath, true);

                        using (var assembly = AssemblyDefinition.ReadAssembly(versionAssemblyPath))
                        {
                            return extractor(assembly);
                        }
                    }
                    finally
                    {
                        try
                        {
                            File.Delete(versionAssemblyPath);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    File.Delete(packagePath);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"ERROR: Failed to delete file {packagePath}");
                    Console.WriteLine(e);
                }
            }
        }

        private static string ExtractDotnetCoreHash(AssemblyDefinition assembly)
        {
            var informationalVersionAttribute = assembly.CustomAttributes.Where(x => x.AttributeType.Name == nameof(AssemblyInformationalVersionAttribute)).FirstOrDefault();
            var argumentValule = informationalVersionAttribute.ConstructorArguments[0].Value.ToString();

            var commitHashRegex = new Regex("[0-9a-f]{40}");

            var match = commitHashRegex.Match(argumentValule);

            if (match.Success)
            {
                return match.Value;
            }
            else
            {
                return null;
            }
        }

        private static string ExtractAspNetCoreHash(AssemblyDefinition assembly)
        {
            var assemblyMetadata = assembly.CustomAttributes.Where(x => x.AttributeType.Name == nameof(AssemblyMetadataAttribute)).FirstOrDefault(x => x.ConstructorArguments.Any(y => "CommitHash" == y.Value.ToString()));

            if (assemblyMetadata != null)
            {
                return assemblyMetadata.ConstructorArguments[1].Value.ToString();
            }
            else
            {
                return null;
            }
        }

        private static void AssignTags(NewIssue issue, IEnumerable<string> scenarios)
        {
            // Use hashsets to handle duplicates
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var scenario in scenarios)
            {
                foreach (var tag in Tags.Match(scenario))
                {
                    foreach (var label in tag.Labels)
                    {
                        if (!String.IsNullOrWhiteSpace(label))
                        {
                            labels.Add(label);
                        }
                    }

                    foreach (var owner in tag.Owners)
                    {
                        owners.Add(owner);
                    }
                }
            }

            foreach (var label in labels)
            {
                issue.Labels.Add(label);
            }

            if (owners.Any())
            {
                issue.Body += $"\n\n";
            }

            foreach (var owner in owners)
            {
                issue.Body += $"@{owner}\n";
            }
        }

        private static Credentials GetCredentialsForUser(string userName, string token)
        {
            return new Credentials(token);
        }

        private static RsaSecurityKey GetRsaSecurityKeyFromPemKey(string keyText)
        {
            using var rsa = RSA.Create();

            var keyBytes = Convert.FromBase64String(keyText);

            rsa.ImportRSAPrivateKey(keyBytes, out _);

            return new RsaSecurityKey(rsa.ExportParameters(true));
        }

        private static async Task<Credentials> GetCredentialsForApp(string appId, string keyPath, long installId)
        {
            var creds = new SigningCredentials(GetRsaSecurityKeyFromPemKey(_githubAppKey), SecurityAlgorithms.RsaSha256);

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
            return new Credentials(installationToken.Token, AuthenticationType.Bearer);
        }
    }
}
