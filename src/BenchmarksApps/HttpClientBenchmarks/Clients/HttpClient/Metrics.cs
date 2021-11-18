namespace HttpClientBenchmarks;

public class Metrics
{
    public List<long> HeadersTimes { get; set; } = new();
    public List<long> ContentStartTimes { get; set; } = new();
    public List<long> ContentEndTimes { get; set; } = new();
    public long SuccessRequests { get; set; }
    public long BadStatusRequests { get; set; }
    public long ExceptionRequests { get; set; }
    public double MeanRps { get; set; }

    public void Add(Metrics m)
    {
        HeadersTimes.AddRange(m.HeadersTimes);
        ContentStartTimes.AddRange(m.ContentStartTimes);
        ContentEndTimes.AddRange(m.ContentEndTimes);
        SuccessRequests += m.SuccessRequests;
        BadStatusRequests += m.BadStatusRequests;
        ExceptionRequests += m.ExceptionRequests;
        MeanRps += m.MeanRps;
    }
}
