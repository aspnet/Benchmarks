#:sdk Aspire.AppHost.Sdk@13.0.0

var builder = DistributedApplication.CreateBuilder(args);

// Add the postgres database from Dockerfile
var postgres = builder.AddDockerfile("postgres-techempower", "../../../docker/postgres-techempower")
    .WithEndpoint(port: 5432, targetPort: 5432, name: "tcp")
    .WithEnvironment("POSTGRES_USER", "benchmarkdbuser")
    .WithEnvironment("POSTGRES_PASSWORD", "benchmarkdbpass")
    .WithEnvironment("POSTGRES_DB", "hello_world");

var postgresEndpoint = postgres.GetEndpoint("tcp");
var connectionString = ReferenceExpression.Create($"Server={postgresEndpoint.Property(EndpointProperty.Host)};Port={postgresEndpoint.Property(EndpointProperty.Port)};Database=hello_world;User Id=benchmarkdbuser;Password=benchmarkdbpass");

// Add all TechEmpower benchmark applications
// Note: BlazorSSR, Minimal, Mvc only support net8.0. MvcFull targets .NET Framework 4.8.
// PlatformBenchmarks and RazorPages support net8.0;net9.0 multi-targeting.

builder.AddProject("blazorssr", "BlazorSSR/BlazorSSR.csproj")
    .WaitFor(postgres)
    .WithEnvironment("ConnectionString", connectionString)
    .WithUrls(context =>
    {
        var http = context.GetEndpoint("http");
        if (http is not null)
        {
            context.Urls.Add(new() { Url = $"{http.Url}/plaintext", DisplayText = "plaintext" });
            context.Urls.Add(new() { Url = $"{http.Url}/json", DisplayText = "json" });
            context.Urls.Add(new() { Url = $"{http.Url}/fortunes", DisplayText = "fortunes" });
            context.Urls.Add(new() { Url = $"{http.Url}/updates/20", DisplayText = "updates" });
        }
    });

builder.AddProject("minimal", "Minimal/Minimal.csproj")
    .WaitFor(postgres)
    .WithEnvironment("ConnectionString", connectionString)
    .WithUrls(context =>
    {
        var http = context.GetEndpoint("http");
        if (http is not null)
        {
            context.Urls.Add(new() { Url = $"{http.Url}/plaintext", DisplayText = "plaintext" });
            context.Urls.Add(new() { Url = $"{http.Url}/json", DisplayText = "json" });
            context.Urls.Add(new() { Url = $"{http.Url}/fortunes", DisplayText = "fortunes" });
            context.Urls.Add(new() { Url = $"{http.Url}/updates/20", DisplayText = "updates" });
        }
    });

builder.AddProject("mvc", "Mvc/Mvc.csproj")
    .WaitFor(postgres)
    .WithEnvironment("ConnectionString", connectionString)
    .WithUrls(context =>
    {
        var http = context.GetEndpoint("http");
        if (http is not null)
        {
            context.Urls.Add(new() { Url = $"{http.Url}/plaintext", DisplayText = "plaintext" });
            context.Urls.Add(new() { Url = $"{http.Url}/json", DisplayText = "json" });
            context.Urls.Add(new() { Url = $"{http.Url}/fortunes", DisplayText = "fortunes" });
            context.Urls.Add(new() { Url = $"{http.Url}/updates/20", DisplayText = "updates" });
        }
    });

builder.AddProject("platformbenchmarks", "PlatformBenchmarks/PlatformBenchmarks.csproj")
    .WaitFor(postgres)
    .WithEnvironment("ConnectionString", connectionString)
    .WithArgs("--framework", "net9.0")
    .WithArgs("-p:IsDatabase=true")
    .WithEnvironment("DATABASE", "PostgreSql")
    .WithUrls(context =>
    {
        var http = context.GetEndpoint("http");
        if (http is not null)
        {
            context.Urls.Add(new() { Url = $"{http.Url}/plaintext", DisplayText = "plaintext" });
            context.Urls.Add(new() { Url = $"{http.Url}/json", DisplayText = "json" });
            context.Urls.Add(new() { Url = $"{http.Url}/fortunes", DisplayText = "fortunes" });
            context.Urls.Add(new() { Url = $"{http.Url}/updates/20", DisplayText = "updates" });
        }
    });

builder.AddProject("razorpages", "RazorPages/RazorPages.csproj")
    .WaitFor(postgres)
    .WithEnvironment("ConnectionString", connectionString)
    .WithArgs("--framework", "net9.0")
    .WithUrls(context =>
    {
        var http = context.GetEndpoint("http");
        if (http is not null)
        {
            context.Urls.Add(new() { Url = $"{http.Url}/plaintext", DisplayText = "plaintext" });
            context.Urls.Add(new() { Url = $"{http.Url}/json", DisplayText = "json" });
            context.Urls.Add(new() { Url = $"{http.Url}/fortunes", DisplayText = "fortunes" });
            context.Urls.Add(new() { Url = $"{http.Url}/updates/20", DisplayText = "updates" });
        }
    });

builder.Build().Run();
