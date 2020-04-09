using System;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PlatformBenchmarks
{
    internal sealed class SocketPipeReader : PipeReader
    {
        // the biggest request that we can get for TechEmpower Plaintext is around 3k
        // it's simply cheaper to allocate small array and reuse it compared to pooling
        private const int BufferSize = 1024 * 4;

        private Socket _socket;
        private byte[] _array;
        private int _offset;
        private int _length;
        private SocketAwaitableEventArgs _awaitableEventArgs;

        public SocketPipeReader(Socket socket, SocketAwaitableEventArgs awaitableEventArgs)
        {
            _socket = socket;
            _array = new byte[BufferSize];
            _offset = 0;
            _length = 0;
            _awaitableEventArgs = awaitableEventArgs;
        }

        public override void AdvanceTo(SequencePosition consumed) => _offset += consumed.GetInteger();

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => _offset += consumed.GetInteger();

        public override void CancelPendingRead() { } // nop

        public override bool TryRead(out ReadResult result)
        {
            result = default;

            return false;
        }

        public override void Complete(Exception exception = null) {  } // nop

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (_offset == _length)
            {
                // previously entire array was parsed (100% of cases for TechEmpower)
                _offset = 0;
                _length = 0;
            }
            else
            {
                // in theory it's possible
                Array.Resize(ref _array, _array.Length * 2);
            }

            var array = _array;
            var args = _awaitableEventArgs;
            args.SetBuffer(new Memory<byte>(array, _length, array.Length - _length));

            if (_socket.ReceiveAsync(args))
            {
                // ReceiveAsync "returns true if the I/O operation is pending"
                // so we await only in that case (it's ugly but gives nice perf boost in JSON benchmark
                await args;
            }

            _length += args.GetResult();

            return new ReadResult(
                new System.Buffers.ReadOnlySequence<byte>(array, _offset, _length - _offset), 
                isCanceled: false, 
                isCompleted: _length == 0); // FIN
        }
    }
}
