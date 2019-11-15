using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace BenchmarksDriver
{
    internal static class WebUtils
    {
        internal static async Task<string> DownloadFileContentAsync(this HttpClient httpClient, string uri)
        {
            using (var downloadStream = await httpClient.GetStreamAsync(uri))
            {
                using (var stringReader = new StreamReader(downloadStream))
                {
                    return await stringReader.ReadToEndAsync();
                }
            }
        }

        internal static async Task DownloadFileAsync(this HttpClient httpClient, string uri, string serverJobUri, string destinationFileName)
        {
            using (var downloadStream = await httpClient.GetStreamAsync(uri))
            using (var fileStream = File.Create(destinationFileName))
            {
                var downloadTask = downloadStream.CopyToAsync(fileStream);

                while (!downloadTask.IsCompleted)
                {
                    // Ping server job to keep it alive while downloading the file
                    Log.Verbose($"GET {serverJobUri}/touch...");
                    await httpClient.GetAsync(serverJobUri + "/touch");

                    await Task.Delay(1000);
                }

                await downloadTask;
            }
        }
    }
}
