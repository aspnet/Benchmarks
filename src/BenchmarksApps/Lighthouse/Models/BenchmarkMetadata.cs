namespace Lighthouse.Models;

internal sealed class BenchmarkMetadata
{
    public required string Source { get; init; }
    public required string Name { get; init; }
    public required string ShortDescription { get; init; }
    public required string LongDescription { get; init; }
    public required string Format { get; init; }
}
