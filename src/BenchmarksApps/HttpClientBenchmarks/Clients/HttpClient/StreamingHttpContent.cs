using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HttpClientBenchmarks
{

    public class StreamingHttpContent : HttpContent
    {
#if NET5_0_OR_GREATER
        private readonly TaskCompletionSource _completeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
#else
        private readonly TaskCompletionSource<object?> _completeTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
#endif
        private readonly TaskCompletionSource<Stream> _getStreamTcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            _getStreamTcs.TrySetResult(stream);
            await _completeTcs.Task.ConfigureAwait(false);
        }

#if NET5_0_OR_GREATER
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            _getStreamTcs.TrySetResult(stream);

            using (cancellationToken.Register(static s => ((TaskCompletionSource)s!).TrySetCanceled(), _completeTcs))
            {
                await _completeTcs.Task.ConfigureAwait(false);
            }
        }
#endif

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        public Task<Stream> GetStreamAsync()
        {
            return _getStreamTcs.Task;
        }

        public void CompleteStream()
        {
#if NET5_0_OR_GREATER
            _completeTcs.TrySetResult();
#else
            _completeTcs.TrySetResult(null);
#endif
        }
    }
}
