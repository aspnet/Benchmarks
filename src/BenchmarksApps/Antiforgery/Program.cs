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
    Console.WriteLine("'/auth' is called. Generating the antiforgery token.");

    var token = antiforgery.GetAndStoreTokens(ctx);
    ctx.Response.Cookies.Append("XSRF-TOKEN", token.RequestToken!, new CookieOptions { HttpOnly = false });
    return Results.Ok();
});

var validatedTokenInvocation = false;

// POST https://localhost:55471/validateToken
// XSRF-TOKEN: <token retrieved from /auth>
app.MapPost("/validateToken", async (HttpContext ctx, IAntiforgery antiforgery) =>
{
    if (!validatedTokenInvocation)
    {
        Console.WriteLine($"'/validateToken' is called. Headers: {string.Join(",", ctx.Request.Headers.Keys)}; ");
        validatedTokenInvocation = true;
    }

    await antiforgery.ValidateRequestAsync(ctx);
    return Results.Ok();
});

await app.StartAsync();
Console.WriteLine("Application started.");
await app.WaitForShutdownAsync();