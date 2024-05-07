using JsonBenchmarks;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
var app = builder.Build();

app.UseMiddleware<JsonMiddleware>();

await app.StartAsync();

Console.WriteLine("Application started.");

await app.WaitForShutdownAsync();