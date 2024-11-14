var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
var app = builder.Build();

app.UseAntiforgery();

await app.StartAsync();

Console.WriteLine("Application started.");

await app.WaitForShutdownAsync();