using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

namespace TodoApi.Tests;

public sealed class LifecycleTests
{
    [Theory]
    [InlineData(null, 8080)]
    [InlineData("1", 1)]
    [InlineData("8080", 8080)]
    [InlineData("65535", 65535)]
    [InlineData("08080", 8080)]
    public void ValidPortConfiguration(string? value, int expected) =>
        Assert.Equal(expected, Program.ParsePort(value));

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+8080")]
    [InlineData("65536")]
    [InlineData("999999999999999999999")]
    [InlineData("abc")]
    [InlineData(" 8080")]
    [InlineData("8080 ")]
    [InlineData("80.80")]
    [InlineData("\u0661\u0662")]
    public async Task InvalidPortExitsUnsuccessfullyWithoutReadiness(string port)
    {
        using var process = new AppProcess(port);
        await process.WaitForExitAsync();
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains(process.Output, line => line.Contains("Invalid PORT:", StringComparison.Ordinal));
        Assert.DoesNotContain("Application started.", process.Output);
    }

    [Fact]
    public async Task OccupiedPortExitsUnsuccessfullyWithoutReadiness()
    {
        using var occupied = new TcpListener(IPAddress.Any, 0);
        occupied.Start();
        var port = ((IPEndPoint)occupied.LocalEndpoint).Port;
        using var process = new AppProcess(port.ToString(CultureInfo.InvariantCulture));
        await process.WaitForExitAsync();
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains(process.Output, line => line.Contains("Application failed:", StringComparison.Ordinal));
        Assert.Contains(process.Output, line => line.Contains(port.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal));
        Assert.DoesNotContain("Application started.", process.Output);
    }

    [Fact]
    public async Task LocalStartRequestAndGracefulHostShutdown()
    {
        var port = ApiFixture.AvailablePort();
        await using var app = Program.CreateApplication([], port.ToString(CultureInfo.InvariantCulture));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = false;
        var stopped = false;
        app.Lifetime.ApplicationStarted.Register(() => started = true);
        app.Lifetime.ApplicationStopped.Register(() => stopped = true);
        try
        {
            await app.StartAsync(deadline.Token);
            Assert.True(started);
            using var client = ApiFixture.CreateClient(port);
            Assert.Equal("ready", await client.GetStringAsync("/healthz"));
            Assert.Equal(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "minimal-todo-v1.expected.json")),
                await client.GetByteArrayAsync("/todos"));
        }
        finally
        {
            var timer = Stopwatch.StartNew();
            using var stopDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await app.StopAsync(stopDeadline.Token);
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"Shutdown took {timer.Elapsed}.");
        }

        Assert.True(stopped);
        using var connection = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(async () =>
            await connection.ConnectAsync(IPAddress.Loopback, port));
    }

    [LinuxTheory]
    [InlineData(15)]
    [InlineData(2)]
    public async Task LinuxSignalShutsDownReadyProcessWithinTenSeconds(int signal)
    {
        var port = ApiFixture.AvailablePort();
        using var process = new AppProcess(port.ToString(CultureInfo.InvariantCulture));
        await process.WaitForReadyAsync();
        using var client = ApiFixture.CreateClient(port);
        Assert.Equal("ready", await client.GetStringAsync("/healthz"));
        Assert.Equal(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "minimal-todo-v1.expected.json")),
            await client.GetByteArrayAsync("/todos"));

        var timer = Stopwatch.StartNew();
        if (Kill(process.Id, signal) != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not signal the owned API process.");
        }

        await process.WaitForExitAsync();
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"Shutdown took {timer.Elapsed}.");
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(1, process.Output.Count(line => line == "Application started."));
        Assert.Contains(process.Output, line => line.Contains("Application stopped.", StringComparison.Ordinal));
        Assert.DoesNotContain(process.Output, line => line.Contains("Request starting", StringComparison.Ordinal));
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int processId, int signal);
}

public sealed class LinuxTheoryAttribute : TheoryAttribute
{
    public LinuxTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = "Requires native Linux to exercise POSIX SIGTERM/SIGINT delivery; Windows host shutdown is tested separately.";
        }
    }
}
