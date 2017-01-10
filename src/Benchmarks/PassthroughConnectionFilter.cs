// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Server.Kestrel.Adapter;
using Microsoft.AspNetCore.Http.Features;

namespace Benchmarks
{
    public class PassthroughConnectionFilter : IConnectionAdapter
    {
        public Task<IAdaptedConnection> OnConnectionAsync(ConnectionAdapterContext context)
        {
            var adapted = new AdaptedConnection(new PassthroughStream(context.ConnectionStream));
            return Task.FromResult<IAdaptedConnection>(adapted);
        }

        private class AdaptedConnection : IAdaptedConnection
        {
            public AdaptedConnection(Stream stream)
            {
                ConnectionStream = stream;
            }

            public Stream ConnectionStream { get; }

            public void PrepareRequest(IFeatureCollection requestFeatures)
            {
            }
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
