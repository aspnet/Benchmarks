using Microsoft.AspNetCore.Antiforgery;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddAntiforgery(options => options.HeaderName = "XSRF-TOKEN");

var app = builder.Build();
app.UseAntiforgery();

app.MapGet("/", () => Results.Ok("hello world!"));
app.MapGet("/noOp", (HttpContext ctx, IAntiforgery antiforgery) => Results.Ok());

// GET https://localhost:55471/auth
app.MapGet("/auth", (HttpContext ctx, IAntiforgery antiforgery) =>
{
    var token = antiforgery.GetAndStoreTokens(ctx);
    ctx.Response.Headers.Append("XSRF-TOKEN", token.RequestToken!);
    return Results.Ok();
});

// POST https://localhost:55471/validateToken
app.MapPost("/validateToken", async (HttpContext ctx, IAntiforgery antiforgery) =>
{
    // HttpContext is expected to have 2 headers:
    // 1) antiforgery token ("XSRF-TOKEN");
    // 2) cookie token ("Cookie") with value of `.AspNetCore.Antiforgery.<unique-sequence>=<cookie_header>`

    await antiforgery.ValidateRequestAsync(ctx);    
    return Results.Ok();
});

await app.StartAsync();
Console.WriteLine("Application started.");
await app.WaitForShutdownAsync();