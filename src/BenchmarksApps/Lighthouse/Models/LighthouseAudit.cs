namespace Lighthouse.Models;

internal sealed class LighthouseAudit
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required double Score { get; init; }
    public required double NumericValue { get; init; }
    public required string NumericUnit { get; init; }
}
