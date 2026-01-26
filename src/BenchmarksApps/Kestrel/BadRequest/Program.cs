using Microsoft.AspNetCore.Server.Kestrel.Core;

// Parse silentClose option before building
var silentClose = args.Any(a => a.Equals("--silentClose", StringComparison.OrdinalIgnoreCase) || 
                                 a.Equals("--silentClose=true", StringComparison.OrdinalIgnoreCase));

// Filter out our custom args before passing to WebApplication
var filteredArgs = args.Where(a => !a.StartsWith("--silentClose", StringComparison.OrdinalIgnoreCase)).ToArray();

// Enable silent close for security-sensitive malformed requests
AppContext.SetSwitch("Microsoft.AspNetCore.Server.Kestrel.SilentCloseOnMalformedRequest", silentClose);

var builder = WebApplication.CreateBuilder(filteredArgs);

// Configure Kestrel for benchmarking
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

// Suppress logging for benchmark accuracy
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var app = builder.Build();

Console.WriteLine($"SilentCloseOnMalformedRequest: {silentClose}");

// Baseline endpoint for valid requests
app.MapGet("/", () => "OK");

// Health check
app.MapGet("/health", () => Results.Ok("healthy"));

app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("Application started."));

app.Run();
