namespace SocketBenchmarks.Clients.Basic;

class Program
{
    public static async Task<int> Main(string[] args)
    {
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, 5678);
        await socket.SendAsync("Hello, World!"u8.ToArray());
        return 0;
    }
}