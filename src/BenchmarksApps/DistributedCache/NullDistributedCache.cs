using Microsoft.Extensions.Caching.Distributed;

namespace DistributedCacheBenchmarks;

public class NullDistributedCache : IDistributedCache
{
    public byte[]? Get(string key)
    {
        return Array.Empty<byte>();
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        return Task.FromResult<byte[]?>(Array.Empty<byte>());
    }

    public void Refresh(string key)
    {
        return;
    }

    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        return;
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        return;
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }
}
