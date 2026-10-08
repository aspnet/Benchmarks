// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Net.Http.Headers;
using System.Text.Json;

namespace Crank.PerfLabExporter.Conversion
{
    public sealed class GitHubCommitTimeResolver : ICommitTimeResolver
    {
        private readonly HttpClient _httpClient;
        private readonly string? _token;

        public GitHubCommitTimeResolver(HttpClient httpClient, string? token)
        {
            _httpClient = httpClient;
            _token = token;
        }

        public async Task<DateTimeOffset> ResolveAsync(
            string repository,
            string commitHash,
            CancellationToken cancellationToken)
        {
            if (!RepositoryIdentity.TryGetGitHubRepository(repository, out var owner, out var name))
            {
                throw new CrankConversionException(
                    $"The runtime commit timestamp was not supplied and repository '{repository}' is not a GitHub repository.");
            }

            var requestUri = new Uri(
                $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/commits/{Uri.EscapeDataString(commitHash)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("aspnet-benchmarks-crank-perflab-exporter/1.0");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            if (!string.IsNullOrWhiteSpace(_token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new CrankConversionException(
                    $"GitHub did not return the runtime commit timestamp for '{commitHash}' ({(int)response.StatusCode} {response.ReasonPhrase}).");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            if (TryReadDate(document.RootElement, "committer", out var timestamp) ||
                TryReadDate(document.RootElement, "author", out timestamp))
            {
                return timestamp;
            }

            throw new CrankConversionException(
                $"GitHub returned runtime commit '{commitHash}' without an author or committer timestamp.");
        }

        private static bool TryReadDate(JsonElement root, string identity, out DateTimeOffset timestamp)
        {
            timestamp = default;
            return root.TryGetProperty("commit", out var commit) &&
                commit.TryGetProperty(identity, out var author) &&
                author.TryGetProperty("date", out var date) &&
                date.ValueKind == JsonValueKind.String &&
                date.TryGetDateTimeOffset(out timestamp);
        }
    }
}
