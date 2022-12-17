namespace DistributedCacheBenchmarks;

public class DistributedCacheOptions
{
    // Length of the keys
    public int KeyLength { get; set; } = 16;

    // Length of the content
    public int ContentLength { get; set; } = 64;

    // Number of cached items
    public int CacheCount { get; set; } = 256;

    // Type of cache implementation
    public string Cache { get; set; } = "distributed";

    // Write cache ratio. E.g., 0.01 means 1 operation
    // out of 100 is a write. 0 means no writes, and 100 means only writes.
    public double WriteRatio { get; set; } = 0;
}
