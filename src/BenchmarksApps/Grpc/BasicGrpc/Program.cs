using BasicGrpc.Services;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<TodoServiceImpl>();

app.RegisterStartup();
app.Run();