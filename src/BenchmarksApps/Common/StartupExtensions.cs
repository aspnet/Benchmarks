using System.Diagnostics.Tracing;

namespace Microsoft.AspNetCore.Builder;

public static class StartupExtensions
{
    public static void RegisterStartup(this WebApplication app)
    {
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            if (BenchmarksEventSource.Log.IsEnabled())
            {
                BenchmarksEventSource.Log.Started();
            }

            Console.WriteLine("Application started. Press Ctrl+C to shut down.");
        });
    }
}

[EventSource(Name = "BenchmarksEventSource")]
internal class BenchmarksEventSource : EventSource
{
    public static readonly BenchmarksEventSource Log = new();

    [Event(1, Level = EventLevel.Informational)]
    public void Started()
    {
        WriteEvent(1);
    }
}