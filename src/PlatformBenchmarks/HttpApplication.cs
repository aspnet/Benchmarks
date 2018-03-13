using System;
using System.Buffers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace PlatformBenchmarks
{
    public static class HttpApplicationConnectionBuilderExtensions
    {
        public static IConnectionBuilder UseHttpApplication<TConnection>(this IConnectionBuilder builder) where TConnection : HttpConnection, new()
        {
            return builder.Use(next => new HttpApplication<TConnection>().ExecuteAsync);
        }
    }

    public class HttpApplication<TConnection> where TConnection : HttpConnection, new()
    {
        public Task ExecuteAsync(ConnectionContext connection)
        {
            var parser = new HttpParser<HttpConnection>();

            var httpConnection = new TConnection
            {
                Connection = connection,
                Parser = parser
            };
            return httpConnection.ExecuteAsync();
        }
    }

    public class HttpConnection : IHttpHeadersHandler, IHttpRequestLineHandler
    {
        private State _state;

        public ConnectionContext Connection { get; set; }

        internal HttpParser<HttpConnection> Parser { get; set; }

        public virtual void OnHeader(Span<byte> name, Span<byte> value)
        {

        }

        public virtual void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
        {

        }

        public virtual ValueTask ProcessRequestAsync()
        {
            return default;
        }

        public virtual ValueTask OnReadCompletedAsync()
        {
            return default;
        }

        public async Task ExecuteAsync()
        {
            try
            {
                while (true)
                {
                    var task = Connection.Transport.Input.ReadAsync();

                    if (!task.IsCompleted)
                    {
                        // No more data in the input
                        await OnReadCompletedAsync();
                    }

                    var result = await task;
                    var buffer = result.Buffer;
                    var consumed = buffer.Start;
                    var examined = buffer.End;

                    try
                    {
                        if (!buffer.IsEmpty)
                        {
                            ParseHttpRequest(buffer, out consumed, out examined);

                            if (_state != State.Body && result.IsCompleted)
                            {
                                throw new InvalidOperationException("Unexpected end of data!");
                            }
                        }
                        else if (result.IsCompleted)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        Connection.Transport.Input.AdvanceTo(consumed, examined);
                    }

                    if (_state == State.Body)
                    {
                        await ProcessRequestAsync();

                        _state = State.StartLine;
                    }
                }

                Connection.Transport.Input.Complete();
            }
            catch (Exception ex)
            {
                Connection.Transport.Input.Complete(ex);
            }
            finally
            {
                Connection.Transport.Output.Complete();
            }
        }

        private void ParseHttpRequest(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
        {
            consumed = buffer.Start;
            examined = buffer.End;

            if (_state == State.StartLine)
            {
                if (Parser.ParseRequestLine(this, buffer, out consumed, out examined))
                {
                    _state = State.Headers;
                    buffer = buffer.Slice(consumed);
                }
            }

            if (_state == State.Headers)
            {
                if (Parser.ParseHeaders(this, buffer, out consumed, out examined, out int consumedBytes))
                {
                    _state = State.Body;
                }
            }
        }

        private enum State
        {
            StartLine,
            Headers,
            Body
        }
    }
}
