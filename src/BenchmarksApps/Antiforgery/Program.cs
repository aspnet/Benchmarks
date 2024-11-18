using Microsoft.AspNetCore.Antiforgery;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddAntiforgery(options => options.HeaderName = "XSRF-TOKEN");

var app = builder.Build();
app.UseAntiforgery();

app.MapGet("/", () => Results.Ok("hello world!"));

// GET https://localhost:55471/auth
app.MapGet("/auth", (HttpContext ctx, IAntiforgery antiforgery) =>
{
    var token = antiforgery.GetAndStoreTokens(ctx);
    ctx.Response.Headers.Append("XSRF-TOKEN", token.RequestToken!);
    Log($"'/auth' is called. Generating the antiforgery token. len='{token.RequestToken?.Length}'");
    return Results.Ok();
});

// POST https://localhost:55471/validateToken
// XSRF-TOKEN: <token retrieved from /auth>
app.MapPost("/validateToken", async (HttpContext ctx, IAntiforgery antiforgery) =>
{
    var xsrf = ctx.Request.Headers["XSRF-TOKEN"].FirstOrDefault();
    var cookie = ctx.Request.Headers["Cookie"].FirstOrDefault();
    Log($"'/validateToken' is called. Headers: {string.Join(",", ctx.Request.Headers.Keys)}; cookie: '{cookie}'; xsrf: '{xsrf}'");

    await antiforgery.ValidateRequestAsync(ctx);    
    return Results.Ok();
});

await app.StartAsync();
Log("Application started.");
await app.WaitForShutdownAsync();

void Log(string message)
    => Console.WriteLine($"[{DateTime.UtcNow.ToString("T")}] {message}");