using System;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PlatformBenchmarks
{
    internal sealed class SocketPipeWriter : PipeWriter
    {
        // the biggest response that we can create for TechEmpower Plaintext is around 1200 bytes
        // it's simply cheaper to allocate small array and reuse it compared to pooling
        private const int BufferSize = 2 * 1024;

        private Socket _socket;
        private byte[] _array;
        private int _offset;

        public SocketPipeWriter(Socket socket)
        {
            _socket = socket;
            _array = new byte[BufferSize];
            _offset = 0;
        }

        public override void Advance(int bytes) => _offset += bytes;

        public override void CancelPendingFlush() { } // nop

        public override void Complete(Exception exception = null) => _offset = 0;

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default) 
            => _offset <= BufferSize ? SendSync() : SendAsync(cancellationToken);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ValueTask<FlushResult> SendSync()
        {
            // we take advantage of the fact that all writes in TE are always small and non-blocking
            // so we perform a SYNC send on purpose

            int start = 0;
            int toSent = _offset;

            do
            {
                int bytesSent = _socket.Send(new ReadOnlySpan<byte>(_array, start, toSent), SocketFlags.None);

                start += bytesSent;
                toSent -= bytesSent;
            }
            while (toSent > 0);

            _offset = 0;

            return new ValueTask<FlushResult>(new FlushResult(isCanceled: false, isCompleted: true));
        }

        private async ValueTask<FlushResult> SendAsync(CancellationToken cancellationToken)
        {
            var isCompleted = await _socket.SendAsync(new ReadOnlyMemory<byte>(_array, 0, _offset), SocketFlags.None) == _offset;

            _offset = 0;

            return new FlushResult(isCanceled: cancellationToken.IsCancellationRequested, isCompleted: isCompleted);
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            ResizeIfNeeded(sizeHint);

            return new Memory<byte>(_array, _offset, _array.Length - _offset);
        }

        public override Span<byte> GetSpan(int sizeHint = 0)
        {
            ResizeIfNeeded(sizeHint);

            return new Span<byte>(_array, _offset, _array.Length - _offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResizeIfNeeded(int sizeHint)
        {
            if (sizeHint >= _array.Length)
            {
                Array.Resize(ref _array, Math.Max(sizeHint, _array.Length * 2));
            }
        }
    }
}
