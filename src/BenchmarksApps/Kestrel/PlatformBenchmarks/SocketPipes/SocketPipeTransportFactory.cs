using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Options;

namespace PlatformBenchmarks
{
    // a copy of https://github.com/dotnet/aspnetcore/blob/master/src/Servers/Kestrel/Transport.Sockets/src/SocketTransportFactory.cs
    // the difference: using different connection listener
    internal sealed class SocketPipeTransportFactory : IConnectionListenerFactory
    {
        private readonly SocketTransportOptions _options;

        public SocketPipeTransportFactory(IOptions<SocketTransportOptions> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _options = options.Value;
        }

        public ValueTask<IConnectionListener> BindAsync(EndPoint endpoint, CancellationToken cancellationToken = default)
        {
            var transport = new SocketPipeConnectionListener(endpoint, _options);
            transport.Bind();
            return new ValueTask<IConnectionListener>(transport);
        }
    }
}