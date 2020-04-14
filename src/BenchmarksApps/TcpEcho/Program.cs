using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;

namespace TcpEcho
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var optionIp = app.Option("-a|--address <IP>", "The server IP address", CommandOptionType.SingleValue);
            var optionPort = app.Option("-p|--port <PORT>", "The server port. Default is 5201", CommandOptionType.SingleValue);
            var optionBacklog = app.Option("-b|--backlog <BACKLOG>", "The TCP backlog. Default is 128.", CommandOptionType.SingleValue);
            var optionType = app.Option("-t|--type <TYPE>", "The server implementation type. Value values are raw, socketpipe, socketduplexpipe.", CommandOptionType.SingleValue);

            app.OnExecuteAsync(async cancellationToken =>
            {
                var ip = IPAddress.Parse(optionIp.Value() ?? "127.0.0.1");
                var port = 8081;
                var backlog = 128;

                if (optionPort.HasValue())
                {
                    port = int.Parse(optionPort.Value());
                }

                if (optionBacklog.HasValue())
                {
                    backlog = int.Parse(optionBacklog.Value());
                }

                Func<Socket, IThreadPoolWorkItem> createWorkItem = socket => new SocketConnection(socket);

                var type = optionType.HasValue() ? optionType.Value() : null;

                switch (type)
                {
                    case "raw":
                        break;
                    case "socketpipe":
                        createWorkItem = socket => new SocketPipeConnection(socket);
                        break;
                    case "socketduplexpipe":
                        createWorkItem = socket => new SocketDuplexPipeConnection(socket);
                        break;
                }

                var endpoint = new IPEndPoint(ip, port);

                Console.WriteLine($"Binding to {endpoint}");

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(endpoint);
                socket.Listen(backlog);

                using var reg = cancellationToken.Register(() => socket.Dispose());

                try
                {
                    while (true)
                    {
                        var connection = await socket.AcceptAsync();
                        var workItem = createWorkItem(connection);
                        ThreadPool.UnsafeQueueUserWorkItem(workItem, preferLocal: false);
                    }
                }
                catch (SocketException)
                {
                    
                }
            });

            await app.ExecuteAsync(args);
        }
    }
}
