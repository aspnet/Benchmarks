// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Crank.PerfLabExporter.Conversion
{
    internal sealed class CachingCommitTimeResolver : ICommitTimeResolver
    {
        private readonly ICommitTimeResolver _inner;
        private readonly Dictionary<string, DateTimeOffset> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public CachingCommitTimeResolver(ICommitTimeResolver inner)
        {
            _inner = inner;
        }

        public async Task<DateTimeOffset> ResolveAsync(
            string repository,
            string commitHash,
            CancellationToken cancellationToken)
        {
            var key =
                $"{RepositoryIdentity.NormalizeRepository(repository)}|{commitHash.Trim()}";
            if (_cache.TryGetValue(key, out var timestamp))
            {
                return timestamp;
            }

            timestamp = await _inner.ResolveAsync(
                repository,
                commitHash,
                cancellationToken);
            _cache[key] = timestamp;
            return timestamp;
        }
    }
}
