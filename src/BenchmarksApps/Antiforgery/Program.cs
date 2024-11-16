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
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.ResponseHeaders;
    options.CombineLogs = true;
});

builder.Services.AddAntiforgery(options => options.HeaderName = "XSRF-TOKEN");

var app = builder.Build();

app.UseHttpLogging();
app.UseAntiforgery();

app.MapGet("/", () => Results.Ok("hello world!"));

app.MapGet("/getAndValidateToken", async (HttpContext ctx, IAntiforgery antiforgery) =>
{
    if (!ctx.Request.Cookies.ContainsKey("XSRF-TOKEN"))
    {
        var token = antiforgery.GetAndStoreTokens(ctx);
        ctx.Response.Cookies.Append("XSRF-TOKEN", token.RequestToken!, new CookieOptions { HttpOnly = false });
        return Results.Ok();
    }

    await antiforgery.ValidateRequestAsync(ctx);
    return Results.Ok();
});

app.Run();