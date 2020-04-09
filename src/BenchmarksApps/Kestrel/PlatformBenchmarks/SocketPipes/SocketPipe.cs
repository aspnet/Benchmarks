using System.IO.Pipelines;
using System.Net.Sockets;

namespace PlatformBenchmarks
{
    internal sealed class SocketPipe : IDuplexPipe
    {
        internal SocketPipe(Socket socket, SocketAwaitableEventArgs awaitableEventArgs)
        {
            Input = new SocketPipeReader(socket, awaitableEventArgs);
            Output = new SocketPipeWriter(socket);
        }

        public PipeReader Input { get; }

        public PipeWriter Output { get; }
    }
}
