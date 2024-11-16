using Microsoft.AspNetCore.Antiforgery;

var builder = WebApplication.CreateBuilder();

builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");

var app = builder.Build();

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