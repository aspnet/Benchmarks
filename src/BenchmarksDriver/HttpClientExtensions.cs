using System;
using System.Net.Http;

namespace BenchmarksDriver
{
    internal static class HttpClientExtensions
    {
        public static HttpResponseMessage EnsureSuccessful(this HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            string responseContent;
            try
            {
                responseContent = response.Content.ReadAsStringAsync().Result;
            }
            catch
            {
                // Print the default error
                response.EnsureSuccessStatusCode();
                return response;
            }

            throw new InvalidOperationException($"Client returned status code '{response.StatusCode}'."
                + Environment.NewLine
                + responseContent);
        }
    }
}
