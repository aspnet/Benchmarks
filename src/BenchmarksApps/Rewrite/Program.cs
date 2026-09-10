using System.Data.Common;
using Microsoft.AspNetCore.Rewrite;
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


await using var dataSource = NpgsqlDataSource.Create(connectionString);

await using var command = dataSource.CreateCommand("SELECT id,regex,replacement FROM \"RewriteRule\"");
await using var reader = await command.ExecuteReaderAsync();

List<RewriteRule> rewriteRules = new();

while (await reader.ReadAsync())
{
    RewriteRule rule = new RewriteRule
    {
        Id = reader.GetFieldValue<int>(0),
        Regex = reader.GetFieldValue<string>(1),
        Replacement = reader.GetFieldValue<string>(2)
    };
    rewriteRules.Add(rule);
};
var options = new RewriteOptions();

foreach(RewriteRule rewriteRule in rewriteRules)
{
    options.AddRewrite(rewriteRule.Regex, rewriteRule.Replacement,skipRemainingRules: false);
}


WebApplication app = builder.Build();

app.UseRewriter(options);



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


app.MapGet("/rewrite2", () =>
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
