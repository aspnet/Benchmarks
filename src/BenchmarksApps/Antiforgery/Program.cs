using Microsoft.AspNetCore.Antiforgery;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();

// "csrf" exercises the auto-injected cross-origin (Sec-Fetch) CSRF protection in isolation,
// so the token-based antiforgery services/middleware are left out to avoid overriding its verdict.
var scenario = builder.Configuration["scenario"] ?? "antiforgery";
var tokenAntiforgeryEnabled = !string.Equals(scenario, "csrf", StringComparison.OrdinalIgnoreCase);

if (tokenAntiforgeryEnabled)
{
    builder.Services.AddAntiforgery(options => options.HeaderName = "XSRF-TOKEN");
}

var app = builder.Build();

if (tokenAntiforgeryEnabled)
{
    app.UseAntiforgery();
}

app.MapGet("/", () => Results.Ok("hello world!"));

// POST endpoint guarded only by the auto-injected cross-origin CSRF protection.
// Sec-Fetch-Site: same-origin/none => 200; cross-site/same-site => 400.
app.MapPost("/csrf", () => Results.Ok());

// Token-based antiforgery endpoints. These depend on IAntiforgery, which is only
// registered when the token-based antiforgery services are added above.
if (tokenAntiforgeryEnabled)
{
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
}

await app.StartAsync();
Console.WriteLine("Application started.");
await app.WaitForShutdownAsync();