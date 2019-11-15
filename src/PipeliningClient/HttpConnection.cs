using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Extensions;

namespace PipeliningClient
{
    public class HttpConnection : IDisposable
    {
        private readonly string _url;
        private readonly int _pipelineDepth;
        private readonly Memory<byte> _requestBytes;
        private readonly Socket _socket;
        private readonly IPEndPoint _hostEndPoint;
        private readonly Pipe _pipe;

        private static ReadOnlySpan<byte> Http11 => new byte[] { (byte)'H', (byte)'T', (byte)'T', (byte)'P', (byte)'/', (byte)'1', (byte)'.', (byte)'1' };
        private static ReadOnlySpan<byte> ContentLength => new byte[] { (byte)'C', (byte)'o', (byte)'n', (byte)'t', (byte)'e', (byte)'n', (byte)'t', (byte)'-', (byte)'L', (byte)'e', (byte)'n', (byte)'g', (byte)'t', (byte)'h' };
        private static ReadOnlySpan<byte> NewLine => new byte[] { (byte)'\r', (byte)'\n' };

        private readonly HttpResponse[] _responses;

        public HttpConnection(string url, int pipelineDepth, IEnumerable<string> headers)
        {
            _url = url;
            _pipelineDepth = pipelineDepth;
            _responses = Enumerable.Range(1, pipelineDepth).Select(x => new HttpResponse()).ToArray();

            UriHelper.FromAbsolute(_url, out var scheme, out var host, out var path, out var query, out var fragment);

            var request = $"GET {path.Value}/{query.Value} HTTP/1.1\r\n";

            if (!headers.Any(h => h.StartsWith("Host:")))
            {
                request += $"Host: {host.Value}\r\n";
            }

            request += "Content-Length: 0\r\n" +
                String.Join("\r\n", headers) +
                "\r\n";

            var requestPayload = Encoding.UTF8.GetBytes(request);
            var buffer = new byte[requestPayload.Length * pipelineDepth];

            for (var k = 0; k < _pipelineDepth; k++)
            {
                requestPayload.CopyTo(buffer, k * requestPayload.Length);
            }

            _requestBytes = buffer.AsMemory();

            _hostEndPoint = new IPEndPoint(IPAddress.Parse(host.Host), host.Port.Value);

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _pipe = new Pipe();

        }

        public async Task ConnectAsync()
        {
            await _socket.ConnectAsync(_hostEndPoint);
            var writing = FillPipeAsync(_socket, _pipe.Writer);
        }

        public async Task<HttpResponse[]> SendRequestsAsync()
        {
            await _socket.SendAsync(_requestBytes, SocketFlags.None);

            for (var k = 0; k < _pipelineDepth; k++)
            {
                var httpResponse = _responses[k];
                httpResponse.Reset();

                await ReadPipeAsync(_pipe.Reader, httpResponse);

                // Stop sending request if the communication faced a problem (socket error)
                if (httpResponse.State != HttpResponseState.Completed)
                {
                    break;
                }
            }

            return _responses;
        }

        public void Dispose()
        {
            if (_socket != null && _socket.Connected)
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }
        }

        private async Task FillPipeAsync(Socket socket, PipeWriter writer)
        {
            const int minimumBufferSize = 512;

            while (true)
            {
                // Allocate at least 512 bytes from the PipeWriter
                Memory<byte> memory = writer.GetMemory(minimumBufferSize);

                int bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None);

                // Indicates that the server is done with sending more data
                if (bytesRead == 0)
                {
                    break;
                }

                // Tell the PipeWriter how much was read from the Socket
                writer.Advance(bytesRead);

                // Make the data available to the PipeReader
                FlushResult result = await writer.FlushAsync();

                if (result.IsCompleted)
                {
                    break;
                }
            }

            // Tell the PipeReader that there's no more data coming
            writer.Complete();
        }

        private async Task ReadPipeAsync(PipeReader reader, HttpResponse httpResponse)
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync();
                var buffer = result.Buffer;

                ParseHttpResponse(ref buffer, httpResponse, out var examined);

                reader.AdvanceTo(buffer.Start, examined);

                // Stop when the response is complete
                if (httpResponse.State == HttpResponseState.Completed)
                {
                    break;
                }

                // Stop if there is incorrect data
                if (httpResponse.State == HttpResponseState.Error)
                {
                    // Incomplete request, close the connection with an error
                    break;
                }

                // Stop reading if there's no more data coming
                if (result.IsCompleted)
                {
                    if (httpResponse.State != HttpResponseState.Completed)
                    {
                        // Incomplete request, close the connection with an error
                        break;
                    }
                }
            }
        }

        private void ParseHttpResponse(ref ReadOnlySequence<byte> buffer, HttpResponse httpResponse, out SequencePosition examined)
        {
            var sequenceReader = new SequenceReader<byte>(buffer);
            examined = buffer.End;

            switch (httpResponse.State)
            {
                case HttpResponseState.StartLine:
                    if (!sequenceReader.TryReadTo(out ReadOnlySpan<byte> startLine, NewLine))
                    {
                        return;
                    }

                    var space = startLine.IndexOf((byte)' ');

                    if (space == -1)
                    {
                        httpResponse.State = HttpResponseState.Error;
                        return;
                    }

                    var version = startLine.Slice(0, space);

                    if (!version.SequenceEqual(Http11))
                    {
                        httpResponse.State = HttpResponseState.Error;
                        return;
                    }

                    startLine = startLine.Slice(space + 1);

                    space = startLine.IndexOf((byte)' ');

                    if (space == -1 || !Utf8Parser.TryParse(startLine.Slice(0, space), out int statusCode, out _))
                    {
                        httpResponse.State = HttpResponseState.Error;
                        return;
                    }
                    else
                    {
                        httpResponse.StatusCode = statusCode;
                    }

                    // reason phrase
                    // startLine.Slice(space + 1)

                    httpResponse.State = HttpResponseState.Headers;

                    examined = sequenceReader.Position;

                    goto case HttpResponseState.Headers;

                case HttpResponseState.Headers:

                    // Read evey headers
                    while (sequenceReader.TryReadTo(out ReadOnlySpan<byte> headerLine, NewLine))
                    {
                        // Is that the end of the headers?
                        if (headerLine.Length == 0)
                        {
                            examined = sequenceReader.Position;

                            // End of headers

                            if (httpResponse.HasContentLengthHeader)
                            {
                                httpResponse.State = HttpResponseState.Body;

                                goto case HttpResponseState.Body;
                            }

                            httpResponse.State = HttpResponseState.ChunkedBody;

                            goto case HttpResponseState.ChunkedBody;
                        }

                        // Parse the header
                        ParseHeader(headerLine, httpResponse);
                    }

                    examined = sequenceReader.Position;
                    break;

                case HttpResponseState.Body:

                    if (httpResponse.ContentLengthRemaining > 0)
                    {
                        var bytesToRead = Math.Min(httpResponse.ContentLengthRemaining, sequenceReader.Remaining);

                        httpResponse.ContentLengthRemaining -= bytesToRead;

                        sequenceReader.Advance(bytesToRead);

                        examined = sequenceReader.Position;
                    }

                    if (httpResponse.ContentLengthRemaining == 0)
                    {
                        httpResponse.State = HttpResponseState.Completed;
                    }

                    break;

                case HttpResponseState.ChunkedBody:

                    while (true)
                    {
                        // Do we need to continue reading a active chunk?
                        if (httpResponse.LastChunkRemaining > 0)
                        {
                            var bytesToRead = Math.Min(httpResponse.LastChunkRemaining, sequenceReader.Remaining);

                            httpResponse.LastChunkRemaining -= (int)bytesToRead;

                            sequenceReader.Advance(bytesToRead);

                            if (httpResponse.LastChunkRemaining > 0)
                            {
                                examined = sequenceReader.Position;
                                // We need to read more data
                                break;
                            }
                            else if (!TryParseCrlf(ref sequenceReader, httpResponse))
                            {
                                break;
                            }

                            examined = sequenceReader.Position;
                        }
                        else
                        {
                            if (!sequenceReader.TryReadTo(out ReadOnlySpan<byte> chunkSizeText, NewLine))
                            {
                                // Don't have a full chunk yet
                                break;
                            }

                            if (!TryParseChunkPrefix(chunkSizeText, out int chunkSize))
                            {
                                httpResponse.State = HttpResponseState.Error;
                                break;
                            }

                            httpResponse.ContentLength += chunkSize;
                            httpResponse.LastChunkRemaining = chunkSize;

                            // The last chunk is always of size 0
                            if (chunkSize == 0)
                            {
                                // The Body should end with two NewLine
                                if (!TryParseCrlf(ref sequenceReader, httpResponse))
                                {
                                    break;
                                }

                                examined = sequenceReader.Position;
                                httpResponse.State = HttpResponseState.Completed;

                                break;
                            }
                        }
                    }

                    break;
            }

            // Slice whatever we've read so far
            buffer = buffer.Slice(sequenceReader.Position);
        }

        private static bool TryParseCrlf(ref SequenceReader<byte> sequenceReader, HttpResponse httpResponse)
        {
            // Need at least 2 characters in the buffer to make a call
            if (sequenceReader.Remaining < 2)
            {
                return false;
            }

            // We expect a crlf
            if (sequenceReader.IsNext(NewLine, advancePast: true))
            {
                return true;
            }

            // Didn't see that, broken server
            httpResponse.State = HttpResponseState.Error;
            return false;
        }

        private static void ParseHeader(in ReadOnlySpan<byte> headerLine, HttpResponse httpResponse)
        {
            var headerSpan = headerLine;
            var colon = headerSpan.IndexOf((byte)':');

            if (colon == -1)
            {
                httpResponse.State = HttpResponseState.Error;
                return;
            }

            if (!headerSpan.Slice(0, colon).SequenceEqual(ContentLength))
            {
                return;
            }

            httpResponse.HasContentLengthHeader = true;


            var value = headerSpan.Slice(colon + 1).Trim((byte)' ');





            if (Utf8Parser.TryParse(value, out long contentLength, out _))
            {
                httpResponse.ContentLength = contentLength;
                httpResponse.ContentLengthRemaining = contentLength;
            }
            else
            {
                httpResponse.State = HttpResponseState.Error;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseChunkPrefix(in ReadOnlySpan<byte> chunkSizeText, out int chunkSize)
        {
            return Utf8Parser.TryParse(chunkSizeText, out chunkSize, out _, 'x');
        }

        private static bool TryParseContentLength(in ReadOnlySequence<byte> remaining, out int contentLength)
        {
            if (remaining.IsSingleSegment)
            {
                if (!Utf8Parser.TryParse(remaining.FirstSpan.TrimStart((byte)' '), out contentLength, out _))
                {
                    return false;
                }
            }
            else
            {
                Span<byte> contentLengthText = stackalloc byte[128];
                remaining.CopyTo(contentLengthText);

                if (!Utf8Parser.TryParse(contentLengthText.TrimStart((byte)' '), out contentLength, out _))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
