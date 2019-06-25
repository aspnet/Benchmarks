using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using BenchmarksDriver.Serializers;
using CsvHelper;
using CsvHelper.TypeConversion;
using Newtonsoft.Json.Linq;

namespace BenchmarksDriver
{
    internal static class BenchmarkDotNetUtils
    {
        internal static async Task DownloadResultFiles(Uri serverJobUri, HttpClient httpClient, BenchmarkDotNetSerializer serializer)
        {
            await DownloadFiles(serverJobUri, "report.csv", httpClient, (fileName, fileContent) => ParseCSVResults(fileName, fileContent, serializer));
            await DownloadFiles(serverJobUri, "report-github.md", httpClient, (fileName, fileContent) => WriteMarkdownResultTableToConsole(fileContent));
        }

        private static async Task DownloadFiles(Uri serverJobUri, string extension, HttpClient httpClient, Action<string, string> contentHandler)
        {
            var uri = $"{serverJobUri}/list?path={HttpUtility.UrlEncode("BenchmarkDotNet.Artifacts/results/*-")}{extension}";
            var response = await httpClient.GetStringAsync(uri);
            var fileNames = JArray.Parse(response).ToObject<string[]>();

            foreach (var fileName in fileNames)
            {
                try
                {
                    uri = $"{serverJobUri}/download?path={HttpUtility.UrlEncode(fileName)}";

                    Program.LogVerbose($"Downloading file {fileName} from {uri}");

                    var fileContent = await httpClient.DownloadFileContentAsync(uri);

                    contentHandler(fileName, fileContent);
                }
                catch (Exception e)
                {
                    Program.Log($"Error while downloading file {fileName} from {uri}, skipping ...");
                    Program.LogVerbose(e.Message);

                    continue;
                }
            }
        }

        private static void ParseCSVResults(string fileName, string csvFileContent, BenchmarkDotNetSerializer serializer)
        {
            using (var sr = new StringReader(csvFileContent))
            {
                using (var csv = new CsvReader(sr))
                {
                    var isRecordBad = false;

                    csv.Configuration.BadDataFound = context =>
                    {
                        isRecordBad = true;
                    };

                    csv.Configuration.RegisterClassMap<CsvResultMap>();
                    csv.Configuration.TypeConverterOptionsCache.AddOptions(typeof(double), new TypeConverterOptions { NumberStyle = NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint });

                    var localResults = new List<CsvResult>();

                    while (csv.Read())
                    {
                        if (!isRecordBad)
                        {
                            localResults.Add(csv.GetRecord<CsvResult>());
                        }

                        isRecordBad = false;
                    }

                    var className = Path.GetFileNameWithoutExtension(fileName).Split('.').LastOrDefault().Split('-', 2).FirstOrDefault();

                    foreach (var result in localResults)
                    {
                        result.Class = className;
                    }

                    serializer.CsvResults.AddRange(localResults);
                }
            }
        }

        private static void WriteMarkdownResultTableToConsole(string markdownFileContent)
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
