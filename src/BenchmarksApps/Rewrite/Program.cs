using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Rewrite.Configuration;
using Rewrite.Data;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

var hostingConfig = new ConfigurationBuilder()
       .AddJsonFile("hosting.json", optional: true)
       .AddEnvironmentVariables()
       .AddCommandLine(args)
       .Build();

string connectionString = hostingConfig["ConnectionString"] ?? "no Connectionstring found";
string databaseServer = hostingConfig["database"] ?? "no DatabaseServer found";



builder.Services.AddEntityFrameworkNpgsql();
var pgSettings = new NpgsqlConnectionStringBuilder(connectionString);

builder.Services.AddDbContextPool<ApplicationDbContext>(
    options => options
        .UseNpgsql(connectionString,
            o => o.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d)))
        .EnableThreadSafetyChecks(false),
    1024);
;



WebApplication app = builder.Build();

await using AsyncServiceScope scope = app.Services.CreateAsyncScope();

ApplicationDbContext dbContext =
    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();


List<Fortune> fortunes = dbContext.Fortune.ToList();


foreach(Fortune fortune in fortunes)
{
    Console.WriteLine($"Fortune: {fortune.Id} - {fortune.Message}");
}

// Configure the HTTP request pipeline.

string[] summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/rewrite1", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
});

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
