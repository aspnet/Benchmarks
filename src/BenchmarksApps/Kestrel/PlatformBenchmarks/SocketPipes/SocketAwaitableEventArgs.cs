using System;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PlatformBenchmarks
{
    // more or less a copy of https://github.com/dotnet/aspnetcore/blob/master/src/Servers/Kestrel/Transport.Sockets/src/Internal/SocketAwaitableEventArgs.cs
    // but without PipeScheduler (always inlining)
    internal sealed class SocketAwaitableEventArgs : SocketAsyncEventArgs, ICriticalNotifyCompletion
    {
        private static readonly Action _callbackCompleted = () => { };

        private Action _callback;

        public SocketAwaitableEventArgs()
#if NETCOREAPP5_0
            : base(unsafeSuppressExecutionContextFlow: true)
#endif
        {
        }

        public SocketAwaitableEventArgs GetAwaiter() => this;
        public bool IsCompleted => ReferenceEquals(_callback, _callbackCompleted);

        public int GetResult()
        {
            _callback = null;

            if (SocketError != SocketError.Success)
            {
                ThrowSocketException(SocketError);
            }

            return BytesTransferred;

            static void ThrowSocketException(SocketError e)
            {
                throw new SocketException((int)e);
            }
        }

        public void OnCompleted(Action continuation)
        {
            if (ReferenceEquals(_callback, _callbackCompleted) ||
                ReferenceEquals(Interlocked.CompareExchange(ref _callback, continuation, null), _callbackCompleted))
            {
                Task.Run(continuation);
            }
        }

        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

        public void Complete() => OnCompleted(this);

        protected override void OnCompleted(SocketAsyncEventArgs _) => Interlocked.Exchange(ref _callback, _callbackCompleted)?.Invoke();
    }
}