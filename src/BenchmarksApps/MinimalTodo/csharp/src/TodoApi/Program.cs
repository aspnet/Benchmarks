using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace TodoApi;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            await using var app = CreateApplication(args, Environment.GetEnvironmentVariable("PORT"));
            await app.RunAsync();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Application failed: {exception}");
            return 1;
        }
    }

    internal static int ParsePort(string? value)
    {
        if (value is null)
        {
            return 8080;
        }

        if (value.Length == 0 ||
            value.Any(character => character is < '0' or > '9') ||
            !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            throw new ArgumentException("Invalid PORT: expected decimal digits in the range 1 through 65535.");
        }

        return port;
    }

    internal static WebApplication CreateApplication(string[] args, string? portValue)
    {
        var port = ParsePort(portValue);
        ImmutableArray<Todo> todos =
        [
            new(1, "Walk the dog", null, false),
            new(2, "Do the dishes", "2026-01-01", false),
            new(3, "Do the laundry", "2026-01-02", false),
            new(4, "Clean the bathroom", null, false),
            new(5, "Clean the car", "2026-01-03", false)
        ];

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Any, port, listener => listener.Protocols = HttpProtocols.Http1));
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(8));
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            options.SerializerOptions.WriteIndented = false;
        });

        var app = builder.Build();
        app.UseExceptionHandler(handler => handler.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        }));

        app.MapGet("/todos", () => Results.Json(todos));
        app.MapGet("/todos/{id}", (string id) =>
            id.Length == 1 && id[0] is >= '1' and <= '5'
                ? Results.Json(todos[id[0] - '1'])
                : Results.NotFound());
        app.MapGet("/healthz", () => Results.Text("ready", "text/plain", Encoding.UTF8));

        app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started."));
        app.Lifetime.ApplicationStopping.Register(() => app.Logger.LogInformation("Application stopping."));
        app.Lifetime.ApplicationStopped.Register(() => app.Logger.LogInformation("Application stopped."));
        return app;
    }
}
