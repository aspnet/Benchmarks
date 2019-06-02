using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Octokit;

namespace BenchmarksBot
{
    class Program
    {
        const string AspNetCorePackage = "Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv";
        // package-id-lower, version
        static readonly string _aspNetCorePackageFormat = "https://dotnetfeed.blob.core.windows.net/aspnet-aspnetcore/flatcontainer/{0}/{1}/{0}.{1}.nupkg";
        static readonly string _netCoreUrlPrevix = "https://dotnetcli.azureedge.net/dotnet/Runtime/{0}/dotnet-runtime-{0}-win-x64.zip";
        static readonly HttpClient _httpClient = new HttpClient();

        static ProductHeaderValue _productHeaderValue = new ProductHeaderValue("BenchmarksBot");
        static long _repositoryId;
        static string _accessToken;
        static string _username;
        static string _connectionString;
        static HashSet<string> _ignoredScenarios;

        static IReadOnlyList<Issue> _recentIssues;

        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix: "BENCHMARKSBOT_")
                .AddCommandLine(args)
                .Build();

            LoadSettings(config);

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
            notRunning = await RemoveReportedRegressions(notRunning, true, r => $"| {r.Scenario} | {r.OperatingSystem}, {r.Hardware}, {r.Scheme}, {r.WebHost} | {r.DateTimeUtc} |");

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

            Console.WriteLine("Looking for scnearios that are not running...");

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

        private static void LoadSettings(IConfiguration config)
        {
            // Tip: The repository id van be found using this endpoint: https://api.github.com/repos/aspnet/Benchmarks

            long.TryParse(config["RepositoryId"], out _repositoryId);
            _accessToken = config["AccessToken"];
            _username = config["Username"];
            _connectionString = config["ConnectionString"];
            _ignoredScenarios = new HashSet<string>();

            if (_repositoryId == 0)
            {
                throw new ArgumentException("RepositoryId argument is missing or invalid");
            }

            if (String.IsNullOrEmpty(_accessToken))
            {
                throw new ArgumentException("AccessToken argument is missing");
            }

            if (String.IsNullOrEmpty(_username))
            {
                throw new ArgumentException("BotUsername argument is missing");
            }

            if (String.IsNullOrEmpty(_connectionString))
            {
                throw new ArgumentException("ConnectionString argument is missing");
            }

            var ignore = config["Ignore"];

            if (!String.IsNullOrEmpty(ignore))
            {
                _ignoredScenarios = new HashSet<string>(ignore.Split(new [] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            }
        }

        private static async Task CreateRegressionIssue(IEnumerable<Regression> regressions)
        {
            if (regressions == null || !regressions.Any())
            {
                return;
            }

            var client = new GitHubClient(_productHeaderValue);
            client.Credentials = new Credentials(_accessToken);

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

                body.AppendLine($"Microsoft.AspNetCore.App __{r.PreviousAspNetCoreVersion}__");
                body.AppendLine($"Microsoft.NetCore.App __{r.PreviousRuntimeVersion}__");

                body.AppendLine();
                body.AppendLine("After versions:");

                body.AppendLine($"Microsoft.AspNetCore.App __{r.CurrentAspNetCoreVersion}__");
                body.AppendLine($"Microsoft.NetCore.App __{r.CurrentRuntimeVersion}__");

                var aspNetChanged = r.PreviousAspNetCoreVersion != r.CurrentAspNetCoreVersion;
                var runtimeChanged = r.PreviousRuntimeVersion != r.CurrentRuntimeVersion;

                if (aspNetChanged || runtimeChanged)
                {
                    body.AppendLine();
                    body.AppendLine("Commits:");

                    if (aspNetChanged)
                    {
                        if (r.AspNetCoreHashes != null && r.AspNetCoreHashes.Length == 2 && r.AspNetCoreHashes[0] != null && r.AspNetCoreHashes[1] != null)
                        {
                            body.AppendLine();
                            body.AppendLine("__Microsoft.AspNetCore.App__");
                            body.AppendLine($"https://github.com/aspnet/AspNetCore/compare/{r.AspNetCoreHashes[0]}...{r.AspNetCoreHashes[1]}");
                        }
                    }

                    if (runtimeChanged)
                    {
                        if (r.CoreFxHashes != null && r.CoreFxHashes.Length == 2 && r.CoreFxHashes[0] != null && r.CoreFxHashes[1] != null)
                        {
                            body.AppendLine();
                            body.AppendLine("__Microsoft.NetCore.App / Core FX__");
                            body.AppendLine($"https://github.com/dotnet/corefx/compare/{r.CoreFxHashes[0]}...{r.CoreFxHashes[1]}");
                        }

                        if (r.CoreClrHashes != null && r.CoreClrHashes.Length == 2 && r.CoreClrHashes[0] != null && r.CoreClrHashes[1] != null)
                        {
                            body.AppendLine();
                            body.AppendLine("__Microsoft.NetCore.App / Core CLR__");
                            body.AppendLine($"https://github.com/dotnet/coreclr/compare/{r.CoreClrHashes[0]}...{r.CoreClrHashes[1]}");
                        }
                    }
                }
            }

            var title = "Performance regression: " + String.Join(", ", regressions.Select(x => x.Scenario).Take(5));

            if (regressions.Count() > 5)
            {
                title += " ...";
            }

            var createIssue = new NewIssue(title)
            {
                Body = body.ToString()
            };

            createIssue.Labels.Add("perf-regression");

            Console.Write(createIssue.Body);
            
            var issue = await client.Issue.Create(_repositoryId, createIssue);
        }

        private static async Task<IEnumerable<Regression>> FindRegression()
        {
            var regressions = new List<Regression>();

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(Queries.Regressions, connection))
                {
                    await connection.OpenAsync();

                    var start = DateTime.UtcNow;
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
                            PreviousRuntimeVersion = Convert.ToString(reader["PreviousRuntimeVersion"]),
                            CurrentRuntimeVersion = Convert.ToString(reader["CurrentRuntimeVersion"])
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
                using (var command = new SqlCommand(Queries.NotRunning, connection))
                {
                    await connection.OpenAsync();

                    var start = DateTime.UtcNow;
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
            client.Credentials = new Credentials(_accessToken);

            var body = new StringBuilder();
            body.Append("Some scenarios have stopped running:");

            body.AppendLine();
            body.AppendLine();
            body.AppendLine("| Scenario | Environment | Last Run |");
            body.AppendLine("| -------- | ----------- | -------- |");

            foreach (var r in regressions.OrderBy(x => x.Scenario).ThenBy(x => x.DateTimeUtc))
            {
                body.AppendLine($"| {r.Scenario} | {r.OperatingSystem}, {r.Hardware}, {r.Scheme}, {r.WebHost} | {r.DateTimeUtc} |");
            }

            var title = "Scenarios are not running: " + String.Join(", ", regressions.Select(x => x.Scenario).Take(5));

            if (regressions.Count() > 5)
            {
                title += " ...";
            }

            var createIssue = new NewIssue(title)
            {
                Body = body.ToString()
            };

            createIssue.Labels.Add("not-running");

            Console.Write(createIssue.Body);

            var issue = await client.Issue.Create(_repositoryId, createIssue);
        }

        private static async Task<IEnumerable<Regression>> FindErrors()
        {
            var regressions = new List<Regression>();

            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(Queries.Error, connection))
                {
                    await connection.OpenAsync();

                    var start = DateTime.UtcNow;
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
            client.Credentials = new Credentials(_accessToken);

            var body = new StringBuilder();
            body.Append("Some scenarios return errors:");

            body.AppendLine();
            body.AppendLine();
            body.AppendLine("| Scenario | Environment | Last Run | Errors |");
            body.AppendLine("| -------- | ----------- | -------- | ------ |");

            foreach (var r in regressions.OrderBy(x => x.Scenario).ThenBy(x => x.DateTimeUtc))
            {
                body.AppendLine($"| {r.Scenario} | {r.OperatingSystem}, {r.Hardware}, {r.Scheme}, {r.WebHost} | {r.DateTimeUtc} | {r.Errors} |");
            }

            var title = "Bad responses: " + String.Join(", ", regressions.Select(x => x.Scenario).Take(5));

            if (regressions.Count() > 5)
            {
                title += " ...";
            }

            var createIssue = new NewIssue(title)
            {
                Body = body.ToString()
            };

            createIssue.Labels.Add("bad-response");

            Console.Write(createIssue.Body);

            var issue = await client.Issue.Create(_repositoryId, createIssue);
        }

        private static async Task<IReadOnlyList<Issue>> GetRecentIssues()
        {
            if (_recentIssues != null)
            {
                return _recentIssues;
            }

            var client = new GitHubClient(_productHeaderValue);
            client.Credentials = new Credentials(_accessToken);

            var recently = new RepositoryIssueRequest
            {
                Filter = IssueFilter.Created,
                State = ItemStateFilter.All,
                Since = DateTimeOffset.Now.Subtract(TimeSpan.FromDays(8))
            };

            var issues = await client.Issue.GetAllForRepository(_repositoryId, recently);

            return _recentIssues = issues;
        }

        private static async Task<IEnumerable<Regression>> RemoveReportedRegressions(IEnumerable<Regression> regressions, bool ignoreClosedIssues, Func<Regression, string> textToFind)
        {
            if (!regressions.Any())
            {
                return regressions;
            }

            var issues = await GetRecentIssues();

            var filtered = new List<Regression>();

            // Look for the same timestamp in all reported issues
            foreach(var r in regressions)
            {
                var filter = false;
                foreach(var issue in issues)
                {
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
                    r.AspNetCoreHashes = new [] 
                    {
                        await GetAspNetCoreCommitHash(r.PreviousAspNetCoreVersion),
                        await GetAspNetCoreCommitHash(r.CurrentAspNetCoreVersion)
                    };
                }

                if (r.PreviousRuntimeVersion != r.CurrentRuntimeVersion)
                {
                    r.CoreClrHashes = new[]
                    {
                        await GetRuntimeAssemblyCommitHash(r.PreviousRuntimeVersion, "SOS.NETCore.dll"),
                        await GetRuntimeAssemblyCommitHash(r.CurrentRuntimeVersion, "SOS.NETCore.dll")
                    };

                    r.CoreFxHashes = new[]
                    {
                        await GetRuntimeAssemblyCommitHash(r.PreviousRuntimeVersion, "System.Collections.dll"),
                        await GetRuntimeAssemblyCommitHash(r.CurrentRuntimeVersion, "System.Collections.dll")
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

        private static async Task<string> GetAspNetCoreCommitHash(string aspNetCoreVersion)
        {
            var packagePath = Path.GetTempFileName();

            try
            {
                // Download Microsoft.AspNet.App

                var aspNetAppUrl = String.Format(_aspNetCorePackageFormat, AspNetCorePackage, aspNetCoreVersion);
                if (!await DownloadFileAsync(aspNetAppUrl, packagePath))
                {
                    return null;
                }

                // Extract the .nuspec file

                using (var archive = ZipFile.OpenRead(packagePath))
                {
                    var aspNetCoreNuSpecPath = Path.GetTempFileName();

                    try
                    {
                        var entry = archive.GetEntry($"{AspNetCorePackage}.nuspec");
                        entry.ExtractToFile(aspNetCoreNuSpecPath, true);

                        var root = XDocument.Parse(await File.ReadAllTextAsync(aspNetCoreNuSpecPath)).Root;

                        XNamespace xmlns = root.Attribute("xmlns").Value;
                        return root
                            .Element(xmlns + "metadata")
                            .Element(xmlns + "repository")
                            .Attribute("commit").Value;
                    }
                    finally
                    {
                        try
                        {
                            File.Delete(aspNetCoreNuSpecPath);
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

        private static async Task<string> GetRuntimeAssemblyCommitHash(string netCoreAppVersion, string assemblyName)
        {
            var packagePath = Path.GetTempFileName();

            try
            {
                // Download the runtime

                var netCoreAppUrl = String.Format(_netCoreUrlPrevix, netCoreAppVersion);
                if (!await DownloadFileAsync(netCoreAppUrl, packagePath))
                {
                    return null;
                }

                // Extract the .nuspec file

                using (var archive = ZipFile.OpenRead(packagePath))
                {
                    var versionAssemblyPath = Path.GetTempFileName();

                    try
                    {
                        var entry = archive.GetEntry($@"shared\Microsoft.NETCore.App\{netCoreAppVersion}\{assemblyName}")
                            ?? archive.GetEntry($@"shared/Microsoft.NETCore.App/{netCoreAppVersion}/{assemblyName}");

                        if (entry == null)
                        {
                            throw new InvalidDataException($"'{netCoreAppUrl}' doesn't contain 'shared/Microsoft.NETCore.App/{netCoreAppVersion}/{assemblyName}'");
                        }

                        entry.ExtractToFile(versionAssemblyPath, true);

                        using (var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(versionAssemblyPath))
                        {
                            var informationalVersionAttribute = assembly.CustomAttributes.Where(x => x.AttributeType.Name == "AssemblyInformationalVersionAttribute").FirstOrDefault();
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
    }
}
