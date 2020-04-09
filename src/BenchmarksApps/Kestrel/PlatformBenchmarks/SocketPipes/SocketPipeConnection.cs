using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;

namespace PlatformBenchmarks
{
    public class SocketPipeConnection : ConnectionContext
    {
        public override string ConnectionId { get; set; }
        public override IFeatureCollection Features { get; }
        public override IDictionary<object, object> Items { get; set; }
        public override IDuplexPipe Transport { get; set; }
        private Socket Socket { get; }
        private SocketAwaitableEventArgs AwaitableEventArgs { get; }

        public SocketPipeConnection(Socket socket)
        {
            Socket = socket;
            AwaitableEventArgs = new SocketAwaitableEventArgs();
            Features = new FeatureCollection();
            Transport = new SocketPipe(socket, AwaitableEventArgs);
            LocalEndPoint = socket.LocalEndPoint;
            RemoteEndPoint = socket.RemoteEndPoint;
            ConnectionId = Guid.NewGuid().ToString();
        }

        public override ValueTask DisposeAsync()
        {
            AwaitableEventArgs.Dispose();
            Socket.Dispose();

            return default;
        }
        
    }
}