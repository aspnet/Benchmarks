// The single '/' endpoint randomly executes a GetAsync or SetAsync operation on
// the configured IDistributedCache implementation (Redis, Distributed memory cache, ...).
// There is no latency added since the goal is to measure the raw performance of the cache
// implementation.

using DistributedCacheBenchmarks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);

var cacheOptions = builder.Configuration.Get<DistributedCacheOptions>();

if (cacheOptions == null)
{
    throw new NotSupportedException("Invalid configuration");
}

switch (cacheOptions.Cache.ToLowerInvariant())
{
    case "memory":
        Console.WriteLine("Using DistributedMemoryCache");
        builder.Services.AddDistributedMemoryCache();
        break;

    case "redis":
        Console.WriteLine("Using StackExchangeRedisCache");
        builder.Services.AddStackExchangeRedisCache(setup => {
            setup.Configuration = cacheOptions.RedisEndpoint;
        });
        break;

    default:
        throw new NotSupportedException($"Invalid value for option [Cache]: '{cacheOptions.Cache}'. Supported values: redis, distributed");
}

// Create a random buffer of the requested size
var content = new byte[cacheOptions.ContentLength];
Random.Shared.NextBytes(content);

var app = builder.Build();

// Create an array of random keys made of the chars 'a'..'z';

var keys = Enumerable.Range(1, cacheOptions.CacheCount).Select(x => new String(Enumerable.Range(1, cacheOptions.KeyLength).Select(k => (char)(Random.Shared.Next(26) + 'a')).ToArray())).ToArray();

var cache = app.Services.GetRequiredService<IDistributedCache>();

// Initializes the cache
foreach (var key in keys)
{
    await cache.SetAsync(key, content);
}

var writes = 0;
var reads = 0;

app.MapGet("/", async ([FromServices] IDistributedCache cache) =>
{
    // Pick a random key
    var keyIndex = Random.Shared.Next(cacheOptions.CacheCount);
    var key = keys[keyIndex];

    // Determines if the operation is a get or set (random decision)
    var isWrite = Random.Shared.Next(100) < (int)(cacheOptions.WriteRatio * 100);

    if (isWrite)
    {
        Interlocked.Increment(ref writes);
        await cache.SetAsync(key, content);
    }
    else
    {
        Interlocked.Increment(ref reads);
        var result = await cache.GetAsync(key);
    }

    return Results.Ok();
});

app.Run();

Console.WriteLine($"reads: {reads} / writes: {writes}");
