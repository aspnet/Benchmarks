using System.Text.Json.Serialization;

namespace Lighthouse.Models;

internal sealed class BenchmarkMetadata
{
    public required string Source { get; init; }
    public required string Name { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Operation Reduce { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Operation Aggregate { get; init; }

    public required string ShortDescription { get; init; }
    public required string LongDescription { get; init; }
    public required string Format { get; init; }
}

internal enum Operation
{
    First,
    Last,
    Avg,
    Sum,
    Median,
    Max,
    Min,
    Count,
    All,
    Delta
}
