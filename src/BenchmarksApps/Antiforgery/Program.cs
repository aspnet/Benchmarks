using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

var debug = builder.Configuration.GetValue<bool>("debug");
if (debug)
{
    builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.AspNetCore.Antiforgery.CsrfProtectionMiddleware", LogLevel.Debug);
}
else
{
    builder.Logging.ClearProviders();
}

// "csrf" exercises the auto-injected cross-origin (Sec-Fetch) CSRF protection in isolation,
// so the token-based antiforgery services/middleware are left out to avoid overriding its verdict.
var scenario = builder.Configuration["scenario"] ?? "antiforgery";
var tokenAntiforgeryEnabled = !string.Equals(scenario, "csrf", StringComparison.OrdinalIgnoreCase);
Console.WriteLine($"Scenario: '{scenario}'. Token-based antiforgery enabled: {tokenAntiforgeryEnabled}.");

if (tokenAntiforgeryEnabled)
{
    builder.Services.AddAntiforgery(options => options.HeaderName = "XSRF-TOKEN");
}

var app = builder.Build();

if (debug)
{
    var requestCount = 0;
    app.Use(async (context, next) =>
    {
        var n = Interlocked.Increment(ref requestCount);
        if (n <= 20)
        {
            var request = context.Request;
            Console.WriteLine($"[debug] #{n} {request.Method} {request.Path}{request.QueryString}");
            foreach (var header in request.Headers)
            {
                Console.WriteLine($"[debug] #{n}   {header.Key}: {header.Value}");
            }
        }

        await next(context);

        if (n <= 20)
        {
            Console.WriteLine($"[debug] #{n} -> {context.Response.StatusCode}");
        }
    });
}

if (tokenAntiforgeryEnabled)
{
    app.UseAntiforgery();
}

app.MapGet("/", () => Results.Ok("hello world!"));

// POST endpoint guarded only by the auto-injected cross-origin CSRF protection.
app.MapPost("/csrf", ([FromForm] string name) => Results.Ok());

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