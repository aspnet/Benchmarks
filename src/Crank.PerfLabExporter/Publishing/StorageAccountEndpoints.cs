// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;

namespace Crank.PerfLabExporter.Publishing
{
    public sealed record StorageAccountEndpoints(Uri BlobServiceUri, Uri QueueServiceUri)
    {
        private static readonly Regex AccountNamePattern = new(
            "^[a-z0-9]{3,24}$",
            RegexOptions.CultureInvariant);

        public static StorageAccountEndpoints Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A storage account name or service URI is required.", nameof(value));
            }

            value = value.Trim();
            if (value.Contains("{}", StringComparison.Ordinal))
            {
                return new StorageAccountEndpoints(
                    ParseAbsoluteUri(value.Replace("{}", "blob", StringComparison.Ordinal)),
                    ParseAbsoluteUri(value.Replace("{}", "queue", StringComparison.Ordinal)));
            }

            if (AccountNamePattern.IsMatch(value))
            {
                return new StorageAccountEndpoints(
                    new Uri($"https://{value}.blob.core.windows.net"),
                    new Uri($"https://{value}.queue.core.windows.net"));
            }

            var uri = ParseAbsoluteUri(value);
            var host = uri.Host;
            if (host.Contains(".blob.", StringComparison.OrdinalIgnoreCase))
            {
                return new StorageAccountEndpoints(
                    ServiceRoot(uri),
                    ReplaceService(uri, ".blob.", ".queue."));
            }

            if (host.Contains(".queue.", StringComparison.OrdinalIgnoreCase))
            {
                return new StorageAccountEndpoints(
                    ReplaceService(uri, ".queue.", ".blob."),
                    ServiceRoot(uri));
            }

            throw new ArgumentException(
                "The storage account must be an account name, an HTTPS blob/queue service URI, or a URI template containing '{}'.",
                nameof(value));
        }

        private static Uri ParseAbsoluteUri(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new ArgumentException(
                    "The storage service URI is not a valid HTTPS URI without query or fragment data.");
            }

            return ServiceRoot(uri);
        }

        private static Uri ReplaceService(Uri uri, string current, string replacement)
        {
            var builder = new UriBuilder(ServiceRoot(uri))
            {
                Host = uri.Host.Replace(current, replacement, StringComparison.OrdinalIgnoreCase)
            };
            return builder.Uri;
        }

        private static Uri ServiceRoot(Uri uri)
        {
            return new UriBuilder(uri)
            {
                Path = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            }.Uri;
        }
    }
}
