using System.Collections.Generic;

namespace HttpClientBenchmarks
{
    public class Metrics
    {
        public List<long> HeadersTimes { get; set; }
        public List<long> ContentStartTimes { get; set; }
        public List<long> ContentEndTimes { get; set; }
        public long SuccessRequests { get; set; }
        public long BadStatusRequests { get; set; }
        public long ExceptionRequests { get; set; }
        public double MeanRps { get; set; }

        public Metrics()
        {
            HeadersTimes = new List<long>();
            ContentStartTimes = new List<long>();
            ContentEndTimes = new List<long>();
        }

        public Metrics(int timingsCapacity)
        {
            HeadersTimes = new List<long>(timingsCapacity);
            ContentStartTimes = new List<long>(timingsCapacity);
            ContentEndTimes = new List<long>(timingsCapacity);
        }

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
}