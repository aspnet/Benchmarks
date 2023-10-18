using System;
using System.Net.Sockets;
using System.CommandLine;
using System.Net;

namespace SocketBenchmarks.Servers.Basic;

class Program
{
    public static async Task<int> Main(string[] args)
    {
        int port = 5678;
        using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
        socket.Listen();
        Task task = Task.Run(async () =>
        {
            while (true)
            {
                Socket client = await socket.AcceptAsync();
                var _ = Task.Run(async () =>
                {
                    while (true)
                    {
                        byte[] buffer = new byte[1024];
                        int read = await client.ReceiveAsync(buffer);
                        if (read == 0)
                        {
                            break;
                        }
                        await client.SendAsync(buffer);
                    }
                });
            }
        });
        await Task.Delay(2_000);
        Console.WriteLine($"Server started on port {port}");
        await task;
        return 0;
    }
}