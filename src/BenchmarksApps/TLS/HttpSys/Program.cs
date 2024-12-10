using Microsoft.AspNetCore.Server.HttpSys;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();

#pragma warning disable CA1416 // Can be launched only on Windows (HttpSys)
builder.WebHost.UseHttpSys();
#pragma warning restore CA1416 // Can be launched only on Windows (HttpSys)

var app = builder.Build();

app.MapGet("/hello-world", () =>
{
    return Results.Ok("Hello World!");
});

await app.StartAsync();
Console.WriteLine("Application started.");
await app.WaitForShutdownAsync();