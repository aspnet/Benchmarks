using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Formats.Asn1;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.ObjectPool;

namespace Kestrel;

public sealed partial class BenchmarkApp : IHttpApplication<IFeatureCollection>
{
    public IFeatureCollection CreateContext(IFeatureCollection features) => features;

    public Task ProcessRequestAsync(IFeatureCollection features)
    {
        var req = features.GetRequestFeature();
        var res = features.GetResponseFeature();

        if (req.Method != "GET")
        {
            res.StatusCode = StatusCodes.Status405MethodNotAllowed;
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
        res.Headers.ContentType = "text/plain";
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
        res.Headers.ContentType = "text/plain";
        res.Headers.ContentLength = HelloWorldPayload.Length;

        var body = features.GetResponseBodyFeature();
        await body.StartAsync();
        body.Writer.Write(HelloWorldPayload);
        await body.Writer.FlushAsync();
    }

    private static async Task JsonChunked(IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = StatusCodes.Status200OK;
        res.Headers.ContentType = "application/json; charset=utf-8";

        var body = features.GetResponseBodyFeature();
        await body.StartAsync();
        await JsonSerializer.SerializeAsync(body.Writer, new JsonMessage { message = "Hello, World!" }, SerializerContext.JsonMessage);
        await body.Writer.FlushAsync();
    }

    private static async Task JsonString(IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = StatusCodes.Status200OK;
        res.Headers.ContentType = "application/json; charset=utf-8";

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
        res.Headers.ContentType = "application/json; charset=utf-8";

        var messageBytes = JsonSerializer.SerializeToUtf8Bytes(new JsonMessage { message = "Hello, World!" }, SerializerContext.JsonMessage);
        res.Headers.ContentLength = messageBytes.Length;

        var body = features.GetResponseBodyFeature();
        await body.StartAsync();

        body.Writer.Write(messageBytes);

        await body.Writer.FlushAsync();
    }

    private static readonly ObjectPoolProvider _objectPoolProvider = new DefaultObjectPoolProvider();
    private static readonly ObjectPool<ArrayBufferWriter<byte>> _bufferWriterPool = _objectPoolProvider.Create<ArrayBufferWriter<byte>>();
    private static readonly ObjectPool<Utf8JsonWriter> _jsonWriterPool = _objectPoolProvider.Create(new Utf8JsonWriterPooledObjectPolicy());

    private static async Task Json(IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = StatusCodes.Status200OK;
        res.Headers.ContentType = "application/json; charset=utf-8";

        //var bufferWriter = _bufferWriterPool.Get();
        //var jsonWriter = _jsonWriterPool.Get();

        //var bufferWriter = new ArrayBufferWriter<byte>(64);
        //await using var jsonWriter = new Utf8JsonWriter(bufferWriter, new() { Indented = false, SkipValidation = true });

        //bufferWriter.ResetWrittenCount();
        //jsonWriter.Reset(bufferWriter);

        //JsonSerializer.Serialize(jsonWriter, new JsonMessage { message = "Hello, World!" }, SerializerContext.JsonMessage);

        var messageSpan = WriteMessage(new JsonMessage { message = "Hello, World!" });
        res.Headers.ContentLength = messageSpan.Length;

        var body = features.GetResponseBodyFeature();

        body.Writer.Write(messageSpan);

        await body.StartAsync();
        await body.Writer.FlushAsync();

        //_jsonWriterPool.Return(jsonWriter);
        //_bufferWriterPool.Return(bufferWriter);
    }

    [ThreadStatic]
    private static ArrayBufferWriter<byte>? _bufferWriter;
    [ThreadStatic]
    private static Utf8JsonWriter? _jsonWriter;

    private static ReadOnlySpan<byte> WriteMessage(JsonMessage message)
    {
        var bufferWriter = _bufferWriter ??= new(64);
        var jsonWriter = _jsonWriter ??= new(_bufferWriter, new() { Indented = false, SkipValidation = true });

        bufferWriter.ResetWrittenCount();
        jsonWriter.Reset(bufferWriter);

        JsonSerializer.Serialize(jsonWriter, new JsonMessage { message = "Hello, World!" }, SerializerContext.JsonMessage);

        return bufferWriter.WrittenSpan;
    }

    private struct JsonMessage
    {
        public required string message { get; set; }
    }

    private class Utf8JsonWriterPooledObjectPolicy : IPooledObjectPolicy<Utf8JsonWriter>
    {
        private static readonly ArrayBufferWriter<byte> _dummyBufferWriter = new(256);

        public Utf8JsonWriter Create() => new(_dummyBufferWriter, new() { Indented = false, SkipValidation = true });

        public bool Return(Utf8JsonWriter obj) => true;
    }

    private static readonly JsonContext SerializerContext = JsonContext.Default;

    // BUG: Can't use GenerationMode = JsonSourceGenerationMode.Serialization here due to https://github.com/dotnet/runtime/issues/111477
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
    [JsonSerializable(typeof(JsonMessage))]
    private partial class JsonContext : JsonSerializerContext
    {

    }
}
