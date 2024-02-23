namespace Lighthouse.Models;

internal static class LighthouseReportExtensions
{
    public static void AddToBenchmarkOutput(this LighthouseReport report, BenchmarkOutput output)
    {
        foreach (var (id, audit) in report.Audits)
        {
            var name = $"lighthouse/{id}";

            output.Metadata.Add(new()
            {
                Name = name,
                Source = BenchmarkOutputSettings.Source,
                LongDescription = audit.Description,
                ShortDescription = $"{audit.Title} ({audit.NumericUnit})",
                Format = "n0",
            });

            output.Measurements.Add(new()
            {
                Name = name,
                Timestamp = report.FetchTime,
                Value = audit.NumericValue,
            });
        }
    }
}
