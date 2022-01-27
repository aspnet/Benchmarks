using System.Net;

namespace HttpClientBenchmarks;

public class StreamingHttpContent : HttpContent
{
    private readonly TaskCompletionSource _completeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Stream> _getStreamTcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        throw new NotSupportedException();
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        _getStreamTcs.TrySetResult(stream);
        await _completeTcs.Task.WaitAsync(cancellationToken);
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
