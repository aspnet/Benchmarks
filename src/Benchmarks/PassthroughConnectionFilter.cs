using Microsoft.AspNetCore.Server.Kestrel.Filter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Benchmarks
{
    public class PassthroughConnectionFilter : IConnectionFilter
    {
        private readonly IConnectionFilter _previous;

        public PassthroughConnectionFilter(IConnectionFilter previous)
        {
            _previous = previous;
        }

        public async Task OnConnectionAsync(ConnectionFilterContext context)
        {
            await _previous.OnConnectionAsync(context);

            context.Connection = new PassthroughStream(context.Connection);
        }

        private class PassthroughStream : Stream
        {
            private Stream _stream;

            public PassthroughStream(Stream stream)
            {
                _stream = stream;
            }

            public override bool CanRead
            {
                get
                {
                    return _stream.CanRead;
                }
            }

            public override bool CanSeek
            {
                get
                {
                    return _stream.CanSeek;
                }
            }

            public override bool CanTimeout
            {
                get
                {
                    return _stream.CanTimeout;
                }
            }

            public override bool CanWrite
            {
                get
                {
                    return _stream.CanWrite;
                }
            }

            public override long Length
            {
                get
                {
                    return _stream.Length;
                }
            }

            public override long Position
            {
                get
                {
                    return _stream.Position;
                }

                set
                {
                    _stream.Position = value;
                }
            }

            public override int ReadTimeout
            {
                get
                {
                    return _stream.ReadTimeout;
                }

                set
                {
                    _stream.ReadTimeout = value;
                }
            }

            public override int WriteTimeout
            {
                get
                {
                    return _stream.WriteTimeout;
                }

                set
                {
                    _stream.WriteTimeout = value;
                }
            }

            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            {
                return _stream.CopyToAsync(destination, bufferSize, cancellationToken);
            }

            public override void Flush()
            {
                _stream.Flush();
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                return _stream.FlushAsync(cancellationToken);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return _stream.Read(buffer, offset, count);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return _stream.ReadAsync(buffer, offset, count, cancellationToken);
            }

            public override int ReadByte()
            {
                return _stream.ReadByte();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return _stream.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                _stream.SetLength(value);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _stream.Write(buffer, offset, count);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return _stream.WriteAsync(buffer, offset, count, cancellationToken);
            }

            public override void WriteByte(byte value)
            {
                _stream.WriteByte(value);
            }
        }
    }
}
