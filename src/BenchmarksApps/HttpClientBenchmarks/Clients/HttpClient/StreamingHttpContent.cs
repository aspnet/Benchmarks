using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HttpClientBenchmarks
{

    public class StreamingHttpContent : HttpContent
    {
        private readonly TaskCompletionSource _completeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<Stream> _getStreamTcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            _getStreamTcs.TrySetResult(stream);
            await _completeTcs.Task.ConfigureAwait(false);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            _getStreamTcs.TrySetResult(stream);

            using (cancellationToken.Register(static s => ((TaskCompletionSource)s!).TrySetCanceled(), _completeTcs))
            {
                await _completeTcs.Task.ConfigureAwait(false);
            }
        }

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
            _completeTcs.TrySetResult();
        }
    }
}
