using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Octokit;

namespace BenchmarksBot
{
    class Program
    {
        static ProductHeaderValue _productHeaderValue = new ProductHeaderValue("BenchmarksBot");
        static string _repository;
        static string _accessToken;
        static string _username;
        static string _connectionString;

        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix: "BENCHMARKSBOT_")
                .AddCommandLine(args)
                .Build();

            LoadSettings(config);

            Console.WriteLine("Looking for regressions...");

            var regressions = await FindRegression();

            foreach(var r in regressions)
            {
                Console.WriteLine(r.ToMarkdownString());
            }

            Console.WriteLine("Excluding the ones already reported...");

            var newRegressions = await RemoveReportedRegressions(regressions);
            
            if (newRegressions.Any())
            {
                Console.WriteLine("Reporting new regressions...");

                await CreateIssue(newRegressions);
            }
            else
            {
                Console.WriteLine("No regressions where found for the current day");
            }
        }

        private static void LoadSettings(IConfiguration config)
        {
            _repository = config["Repository"];
            _accessToken = config["AccessToken"];
            _username = config["Username"];
            _connectionString = config["ConnectionString"];

            if (String.IsNullOrEmpty(_repository))
            {
                throw new ArgumentException("Repository argument is missing");
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
        }

        private static async Task CreateIssue(IEnumerable<Regression> regressions)
        {
            if (regressions == null || !regressions.Any())
            {
                return;
            }

            var client = new GitHubClient(_productHeaderValue);
            client.Credentials = new Credentials(_accessToken);

            var body = new StringBuilder();
            body.Append("A performance regression has been detected for the following scenarios:");
            body.AppendLine();
            body.AppendLine();
            body.AppendLine("| Scenario | Environment | Date | RPS | Std. Dev |");
            body.AppendLine("| -------- | ----------- | ---- | --- | -------- |");

            foreach (var r in regressions.OrderBy(x => x.Scenario).ThenBy(x => x.DateTimeUtc))
            {
                body.AppendLine(r.ToMarkdownString());
            }

            var createIssue = new NewIssue("Performance regression")
            {
                Body = body.ToString()
            };

            var issue = await client.Issue.Create(_username, _repository, createIssue);
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
                            Scenario = reader.GetString(0),
                            Hardware = reader.GetString(1),
                            OperatingSystem = reader.GetString(2),
                            Scheme = reader.GetString(3),
                            WebHost = reader.GetString(4),
                            DateTimeUtc = reader.GetDateTimeOffset(5),
                            Values = new[] { (int)reader.GetDouble(9), (int)reader.GetDouble(8), (int)reader.GetDouble(7), (int)reader.GetDouble(6), (int)reader.GetDouble(10) },
                            Stdev = reader.GetDouble(11)
                        });
                    }                    
                }
            }

            return regressions;
        }

        private static async Task<IEnumerable<Regression>> RemoveReportedRegressions(IEnumerable<Regression> regressions)
        {
            var client = new GitHubClient(_productHeaderValue);
            client.Credentials = new Credentials(_accessToken);

            var recently = new RepositoryIssueRequest
            {
                Filter = IssueFilter.Created,
                State = ItemStateFilter.All,
                Since = DateTimeOffset.Now.Subtract(TimeSpan.FromDays(3))
            };

            var issues = await client.Issue.GetAllForRepository(_username, _repository, recently);

            var filtered = new List<Regression>();

            // Look for the same timestamp in all reported issues
            foreach(var r in regressions)
            {
                var filter = false;
                foreach(var issue in issues)
                {
                    if (issue.Body != null && issue.Body.Contains(r.DateTimeUtc.ToString("u")))
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
    }
}
