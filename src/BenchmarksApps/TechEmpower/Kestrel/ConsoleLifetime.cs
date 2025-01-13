using System.Diagnostics;
using System.Runtime.InteropServices;

public class ConsoleLifetime : IDisposable
{
    private readonly TaskCompletionSource _tcs = new();
    private PosixSignalRegistration? _sigIntRegistration;
    private PosixSignalRegistration? _sigQuitRegistration;
    private PosixSignalRegistration? _sigTermRegistration;

    public ConsoleLifetime()
    {
        if (!OperatingSystem.IsWasi())
        {
            Action<PosixSignalContext> handler = HandlePosixSignal;
            _sigIntRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, handler);
            _sigQuitRegistration = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, handler);
            _sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, handler);

            Console.WriteLine("Application started. Press Ctrl+C to shut down.");
        }
    }

    public Task LifetimeTask => _tcs.Task;

    public void Dispose()
    {
        UnregisterShutdownHandlers();
    }

    private void HandlePosixSignal(PosixSignalContext context)
    {
        Debug.Assert(context.Signal == PosixSignal.SIGINT || context.Signal == PosixSignal.SIGQUIT || context.Signal == PosixSignal.SIGTERM);

        context.Cancel = true;

        _tcs.TrySetResult();
    }

    private void UnregisterShutdownHandlers()
    {
        _sigIntRegistration?.Dispose();
        _sigQuitRegistration?.Dispose();
        _sigTermRegistration?.Dispose();
    }
}
