using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;

namespace Kestrel;

public sealed partial class BenchmarkApp : IHttpApplication<IFeatureCollection>
{
    private const string TextPlainContentType = "text/plain";
    private const string JsonContentTypeWithCharset = "application/json; charset=utf-8";

    public IFeatureCollection CreateContext(IFeatureCollection features) => features;

    public Task ProcessRequestAsync(IFeatureCollection features)
    {
        var req = features.GetRequestFeature();
        var res = features.GetResponseFeature();

        if (req.Method != "GET")
        {
            res.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return Task.CompletedTask;
        }

        return req.Path switch
        {
            "/plaintext" => Plaintext(res, features),
            "/json" => Json(res, features),
            "/json-string" => JsonString(res, features),
            "/json-utf8bytes" => JsonUtf8Bytes(res, features),
            "/json-chunked" => JsonChunked(res, features),
            "/" => Index(res, features),
            _ => NotFound(res, features),
        };
    }

    private static Task NotFound(IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    public void DisposeContext(IFeatureCollection features, Exception? exception) { }

    private static ReadOnlySpan<byte> IndexPayload => "Running directly on Kestrel! Navigate to /plaintext and /json to see other endpoints."u8;

    private static async Task Index(IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = StatusCodes.Status200OK;
        res.Headers.ContentType = TextPlainContentType;
        res.Headers.ContentLength = IndexPayload.Length;

        var body = features.GetResponseBodyFeature();

        await body.StartAsync();
        body.Writer.Write(IndexPayload);
        await body.Writer.FlushAsync();
    }

    private static ReadOnlySpan<byte> HelloWorldPayload => "Hello, World!"u8;

    private static async Task Plaintext(IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = StatusCodes.Status200OK;
        res.Headers.ContentType = TextPlainContentType;
        res.Headers.ContentLength = HelloWorldPayload.Length;

        var body = features.GetResponseBodyFeature();
        await body.StartAsync();
        body.Writer.Write(HelloWorldPayload);
        await body.Writer.FlushAsync();
    }

    private static async Task JsonChunked(IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = StatusCodes.Status200OK;
        res.Headers.ContentType = JsonContentTypeWithCharset;

        var body = features.GetResponseBodyFeature();
        await body.StartAsync();
        await JsonSerializer.SerializeAsync(body.Writer, new JsonMessage { message = "Hello, World!" }, SerializerContext.JsonMessage);
        await body.Writer.FlushAsync();
    }

    private static async Task JsonString(IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = StatusCodes.Status200OK;
        res.Headers.ContentType = JsonContentTypeWithCharset;

        var message = JsonSerializer.Serialize(new JsonMessage { message = "Hello, World!" }, SerializerContext.JsonMessage);
        res.Headers.ContentLength = Encoding.UTF8.GetByteCount(message);

        var body = features.GetResponseBodyFeature();
        await body.StartAsync();

        Span<byte> buffer = stackalloc byte[256];
        var length = Encoding.UTF8.GetBytes(message, buffer);
        body.Writer.Write(buffer[..length]);

        await body.Writer.FlushAsync();
    }

    private static async Task JsonUtf8Bytes(IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = StatusCodes.Status200OK;
        res.Headers.ContentType = JsonContentTypeWithCharset;

        var messageBytes = JsonSerializer.SerializeToUtf8Bytes(new JsonMessage { message = "Hello, World!" }, SerializerContext.JsonMessage);
        res.Headers.ContentLength = messageBytes.Length;

        var body = features.GetResponseBodyFeature();
        await body.StartAsync();

        body.Writer.Write(messageBytes);

        await body.Writer.FlushAsync();
    }

    private static async Task Json(IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = StatusCodes.Status200OK;
        res.Headers.ContentType = JsonContentTypeWithCharset;

        var messageSpan = JsonSerializeToUtf8Span(new JsonMessage { message = "Hello, World!" }, SerializerContext.JsonMessage);
        res.Headers.ContentLength = messageSpan.Length;

        var body = features.GetResponseBodyFeature();

        body.Writer.Write(messageSpan);

        await body.StartAsync();
        await body.Writer.FlushAsync();
    }

    [ThreadStatic]
    private static ArrayBufferWriter<byte>? _bufferWriter;
    [ThreadStatic]
    private static Utf8JsonWriter? _jsonWriter;

    private static ReadOnlySpan<byte> JsonSerializeToUtf8Span<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        var bufferWriter = _bufferWriter ??= new(64);
        var jsonWriter = _jsonWriter ??= new(_bufferWriter, new() { Indented = false, SkipValidation = true });

        bufferWriter.ResetWrittenCount();
        jsonWriter.Reset(bufferWriter);

        JsonSerializer.Serialize(jsonWriter, value, jsonTypeInfo);

        return bufferWriter.WrittenSpan;
    }

    private struct JsonMessage
    {
        public required string message { get; set; }
    }

    private static readonly JsonContext SerializerContext = JsonContext.Default;

    // BUG: Can't use GenerationMode = JsonSourceGenerationMode.Serialization here due to https://github.com/dotnet/runtime/issues/111477
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
    [JsonSerializable(typeof(JsonMessage))]
    private partial class JsonContext : JsonSerializerContext
    {

    }
}
