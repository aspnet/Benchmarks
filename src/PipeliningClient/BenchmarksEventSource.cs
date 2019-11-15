using System.Diagnostics.Tracing;

namespace PipeliningClient
{
    internal sealed class BenchmarksEventSource : EventSource
    {
        public static readonly BenchmarksEventSource Log = new BenchmarksEventSource();

        internal BenchmarksEventSource()
            : this("Benchmarks")
        {

        }

        // Used for testing
        internal BenchmarksEventSource(string eventSourceName)
            : base(eventSourceName)
        {
        }

        [Event(1, Level = EventLevel.Informational)]
        public void Measure(string name, long value)
        {
            WriteEvent(1, name, value);
        }

        [Event(2, Level = EventLevel.Informational)]
        public void Metadata(string name, string aggregate, string reduce, string shortDescription, string longDescription, string format)
        {
            WriteEvent(2, name, aggregate, reduce, shortDescription, longDescription, format);
        }
    }
}
