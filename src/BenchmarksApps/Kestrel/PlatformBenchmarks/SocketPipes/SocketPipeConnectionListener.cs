using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;

namespace PlatformBenchmarks
{
    // copy of https://github.com/dotnet/aspnetcore/blob/master/src/Servers/Kestrel/Transport.Sockets/src/SocketConnectionListener.cs
    // difference: creates SocketPipeConnection instead of SocketConnection
    internal sealed class SocketPipeConnectionListener : IConnectionListener
    {
        private readonly SocketTransportOptions _options;
        
        private Socket _listenSocket;

        public SocketPipeConnectionListener(EndPoint endpoint, SocketTransportOptions options)
        {
            _options = options;
            EndPoint = endpoint;
        }

        public EndPoint EndPoint { get; private set; }

        public ValueTask UnbindAsync(CancellationToken cancellationToken = new CancellationToken()) => DisposeAsync();

        public ValueTask DisposeAsync()
        {
            _listenSocket?.Dispose();
            return default;
        }

        public async ValueTask<ConnectionContext> AcceptAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            while (true)
            {
                try
                {
                    var acceptSocket = await _listenSocket.AcceptAsync();

                    // Only apply no delay to Tcp based endpoints
                    if (acceptSocket.LocalEndPoint is IPEndPoint)
                    {
                        acceptSocket.NoDelay = _options.NoDelay;
                    }

                    return new SocketPipeConnection(acceptSocket);
                }
                catch (ObjectDisposedException)
                {
                    // A call was made to UnbindAsync/DisposeAsync just return null which signals we're done
                    return null;
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.OperationAborted)
                {
                    // A call was made to UnbindAsync/DisposeAsync just return null which signals we're done
                    return null;
                }
                catch (SocketException se)
                {
                    Console.WriteLine($"Socket exception! {se}");
                    // The connection got reset while it was in the backlog, so we try again.
                    // _trace.ConnectionReset(connectionId: "(null)");
                }
            }
        }

        internal void Bind()
        {
            if (_listenSocket != null)
            {
                throw new InvalidOperationException("TransportAlreadyBound");
            }

            // Check if EndPoint is a FileHandleEndpoint before attempting to access EndPoint.AddressFamily
            // since that will throw an NotImplementedException.
            if (EndPoint is FileHandleEndPoint)
            {
                throw new NotSupportedException("FileHandleEndPointNotSupported");
            }

            Socket listenSocket;

            // Unix domain sockets are unspecified
            var protocolType = EndPoint is UnixDomainSocketEndPoint ? ProtocolType.Unspecified : ProtocolType.Tcp;

            listenSocket = new Socket(EndPoint.AddressFamily, SocketType.Stream, protocolType);

            // Kestrel expects IPv6Any to bind to both IPv6 and IPv4
            if (EndPoint is IPEndPoint ip && ip.Address == IPAddress.IPv6Any)
            {
                listenSocket.DualMode = true;
            }

            try
            {
                listenSocket.Bind(EndPoint);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                throw new AddressInUseException(e.Message, e);
            }

            EndPoint = listenSocket.LocalEndPoint;

#if NETCOREAPP5_0
            listenSocket.Listen(_options.Backlog);
#else
            listenSocket.Listen(512); // default backlog value
#endif

            _listenSocket = listenSocket;
        }
    }
}