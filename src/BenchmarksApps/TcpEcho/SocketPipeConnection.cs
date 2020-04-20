using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpEcho
{
    public class SocketPipeConnection : IThreadPoolWorkItem
    {
        private readonly Socket _socket;

        public SocketPipeConnection(Socket socket)
        {
            _socket = socket;
        }

        public void Execute()
        {
            _ = ExecuteAsync();
        }

        private async Task ExecuteAsync()
        {
            var pipe = new Pipe();

            async Task ReadFromSocket()
            {
                while (true)
                {
                    var buffer = pipe.Writer.GetMemory(2048);
                    var read = await _socket.ReceiveAsync(buffer, SocketFlags.None);

                    if (read == 0)
                    {
                        break;
                    }

                    pipe.Writer.Advance(read);

                    await pipe.Writer.FlushAsync();
                }

                await pipe.Writer.CompleteAsync();
            }

            async Task WriteToSocket()
            {
                while (true)
                {
                    var result = await pipe.Reader.ReadAsync();

                    foreach (var buffer in result.Buffer)
                    {
                        await _socket.SendAsync(buffer, SocketFlags.None);
                    }

                    if (result.IsCompleted)
                    {
                        break;
                    }

                    pipe.Reader.AdvanceTo(result.Buffer.End);
                }

                await pipe.Reader.CompleteAsync();
            }

            var readTask = ReadFromSocket();
            var writeTask = WriteToSocket();

            await readTask;
            await writeTask;
        }
    }
}
