using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpEcho
{
    public class SocketConnection : IThreadPoolWorkItem
    {
        private readonly Socket _socket;
        public SocketConnection(Socket socket)
        {
            _socket = socket;
        }

        public void Execute()
        {
            _ = ExecuteAsync();
        }

        private async Task ExecuteAsync()
        {
            // A hack to dispose things
            using var _ = _socket;

            var buffer = new byte[4096];
            while (true)
            {
                var read = await _socket.ReceiveAsync(buffer, SocketFlags.None);
                if (read == 0)
                {
                    break;
                }

                await _socket.SendAsync(buffer, SocketFlags.None);
            }
        }
    }
}
