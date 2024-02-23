namespace Lighthouse.Models;

internal sealed class BenchmarkOutput
{
    public List<BenchmarkMetadata> Metadata { get; } = [];
    public List<BenchmarkMeasurement> Measurements { get; } = [];
}
