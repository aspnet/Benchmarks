using System.Text.Json.Serialization;
using System.Text.Json;

namespace JsonBenchmarks;

public class JsonMiddleware
{
    private static readonly PathString _path = new PathString("/json");

    private const int _jsonFramingSize = 14;

    private readonly int _bufferSize;
    private readonly RequestDelegate _next;

    private readonly string _message = new string("Hello, World!");

    public JsonMiddleware(RequestDelegate next, IConfiguration config)
    {
        if (!int.TryParse(config["JSONSIZE"], out var length))
        {
            length = _message.Length;
        }
        else
        {
            _message = new string('a', length);
        }
        _next = next;
        _bufferSize = length + _jsonFramingSize;
    }

    public Task Invoke(HttpContext httpContext)
    {
        if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
        {
            httpContext.Response.StatusCode = 200;
            httpContext.Response.ContentLength = _bufferSize;

            return httpContext.Response.WriteAsJsonAsync(new JsonMessage { message = _message }, CustomJsonContext.Default.JsonMessage);
        }

        return _next(httpContext);
    }
}

#if NET8_0_OR_GREATER
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
#else
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
#endif
[JsonSerializable(typeof(JsonMessage))]
internal partial class CustomJsonContext : JsonSerializerContext
{
}

public struct JsonMessage
{
    public string message { get; set; }
}