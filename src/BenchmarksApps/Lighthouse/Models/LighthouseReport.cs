namespace Lighthouse.Models;

internal sealed class LighthouseReport
{
    public required DateTime FetchTime { get; init; }

    public Dictionary<string, LighthouseAudit> Audits { get; init; } = [];
}
