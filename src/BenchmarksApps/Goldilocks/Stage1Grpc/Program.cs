using System.Text;
using Goldilocks.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders(); // Clearing for benchmark scenario, template has AddConsole();
builder.Services.AddGrpc();
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

var app = builder.Build();

app.MapGrpcService<TodoServiceImpl>();
app.Use(async (context, next) =>
{
    var ms = new MemoryStream();
    await context.Request.Body.CopyToAsync(ms);

    var s = Convert.ToBase64String(ms.ToArray());
    await next(context);
});

app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started. Press Ctrl+C to shut down."));
app.Run();