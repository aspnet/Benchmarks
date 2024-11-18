using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpLogging;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = false;
    options.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields =
        // request
        HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestPath
        | HttpLoggingFields.RequestHeaders
        // response
        | HttpLoggingFields.ResponseStatusCode;
    options.CombineLogs = true;
});

builder.Services.AddAntiforgery(options => options.HeaderName = "XSRF-TOKEN");

var app = builder.Build();

app.UseHttpLogging();
app.UseAntiforgery();

app.MapGet("/", () => Results.Ok("hello world!"));

// GET https://localhost:55471/auth
app.MapGet("/auth", (HttpContext ctx, IAntiforgery antiforgery) =>
{
    var token = antiforgery.GetAndStoreTokens(ctx);
    ctx.Response.Cookies.Append("XSRF-TOKEN", token.RequestToken!, new CookieOptions { HttpOnly = false });
    return Results.Ok();
});

// POST https://localhost:55471/validateToken
// XSRF-TOKEN: <token retrieved from /auth>
app.MapPost("/validateToken", async (HttpContext ctx, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(ctx);
    return Results.Ok();
});

app.Run();