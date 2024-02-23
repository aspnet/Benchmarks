namespace Lighthouse.Models;

internal sealed class BenchmarkMeasurement
{
    public required DateTime Timestamp { get; init; }
    public required string Name { get; init; }
    public required object Value { get; init; }
}
