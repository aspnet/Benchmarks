using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;

namespace TodoApi.Tests;

public sealed class ApiFixture : IAsyncLifetime
{
    private WebApplication? app;
    public HttpClient Client { get; private set; } = null!;
    public byte[] Expected { get; } =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "minimal-todo-v1.expected.json"));

    public async Task InitializeAsync()
    {
        var port = AvailablePort();
        app = Program.CreateApplication([], port.ToString(CultureInfo.InvariantCulture));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await app.StartAsync(deadline.Token);
        Client = CreateClient(port);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (app is not null)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await app.StopAsync(deadline.Token);
            }
            finally
            {
                await app.DisposeAsync();
            }
        }
    }

    internal static int AvailablePort()
    {
        using var reservation = new TcpListener(IPAddress.Any, 0);
        reservation.Start();
        return ((IPEndPoint)reservation.LocalEndpoint).Port;
    }

    internal static HttpClient CreateClient(int port) => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseProxy = false
    })
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestVersion = HttpVersion.Version11,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
    };
}
