using System;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TcpEcho
{
    public class SocketDuplexPipeConnection : IDuplexPipe, IThreadPoolWorkItem, IAsyncDisposable
    {
        public PipeReader Input { get; }
        public PipeWriter Output { get; }

        private readonly Task _socketTasks;

        public SocketDuplexPipeConnection(Socket socket)
        {
            var input = new Pipe();
            var output = new Pipe();

            Input = input.Reader;
            Output = output.Writer;

            var readTask = ReadFromSocket();
            var writeTask = WriteToSocket();

            _socketTasks = Task.WhenAll(readTask, writeTask);

            async Task ReadFromSocket()
            {
                while (true)
                {
                    var buffer = input.Writer.GetMemory(2048);
                    var read = await socket.ReceiveAsync(buffer, SocketFlags.None);

                    if (read == 0)
                    {
                        break;
                    }

                    await input.Writer.FlushAsync();
                }

                await input.Writer.CompleteAsync();
            }

            async Task WriteToSocket()
            {
                while (true)
                {
                    var result = await output.Reader.ReadAsync();

                    foreach (var buffer in result.Buffer)
                    {
                        await socket.SendAsync(buffer, SocketFlags.None);
                    }

                    if (result.IsCompleted)
                    {
                        break;
                    }

                    output.Reader.AdvanceTo(result.Buffer.End);
                }

                await output.Reader.CompleteAsync();
            }
        }

        public void Execute()
        {
            _ = ExecuteConnectionAsync();
        }

        private async Task ExecuteConnectionAsync()
        {
            await using (this)
            {
                await Input.CopyToAsync(Output);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Input.CompleteAsync();
            await Output.CompleteAsync();

            await _socketTasks;
        }
    }

}
