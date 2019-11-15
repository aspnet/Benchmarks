namespace PipeliningClient
{
    public enum HttpResponseState
    {
        StartLine,
        Headers,
        Body,
        ChunkedBody,
        Completed,
        Error
    }

    public class HttpResponse
    {
        public HttpResponseState State { get; set; } = HttpResponseState.StartLine;
        public int StatusCode { get; set; }
        public long ContentLength { get; set; }
        public long ContentLengthRemaining { get; set; }
        public bool HasContentLengthHeader { get; set; }
        public int LastChunkRemaining { get; set; }

        public void Reset()
        {
            State = HttpResponseState.StartLine;
            StatusCode = default;
            ContentLength = default;
            ContentLengthRemaining = default;
            HasContentLengthHeader = default;
            LastChunkRemaining = default;
        }
    }
}
