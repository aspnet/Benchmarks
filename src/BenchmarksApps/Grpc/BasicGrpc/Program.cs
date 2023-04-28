using BasicGrpc.Services;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<TodoServiceImpl>();

app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started. Press Ctrl+C to shut down."));
app.Run();