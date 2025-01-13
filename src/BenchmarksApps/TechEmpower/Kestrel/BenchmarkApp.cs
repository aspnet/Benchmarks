using System.Buffers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.ObjectPool;

public class BenchmarkApp : IHttpApplication<IFeatureCollection>
{
    public IFeatureCollection CreateContext(IFeatureCollection features) => features;

    public Task ProcessRequestAsync(IFeatureCollection features)
    {
        var req = features.GetRequestFeature();
        var res = features.GetResponseFeature();

        if (req.Method != "GET")
        {
            res.StatusCode = 405;
            var body = features.GetResponseBodyFeature();
            return body.StartAsync().ContinueWith(t => body.CompleteAsync());
        }

        return req.Path switch
        {
            "/plaintext" => Plaintext(req, res, features),
            "/json" => Json(req, res, features),
            "/" => Index(req, res, features),
            _ => NotFound(req, res, features),
        };
    }

    private static async Task NotFound(IHttpRequestFeature req, IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = 404;
        res.Headers.ContentType = "text/plain";
        res.Headers.ContentLength = HelloWorldPayload.Length;

        var body = features.GetResponseBodyFeature();

        await body.StartAsync();
        body.Writer.Write(HelloWorldPayload);
        await body.CompleteAsync();
    }

    public void DisposeContext(IFeatureCollection features, Exception? exception) { }

    private static ReadOnlySpan<byte> IndexPayload => "Running directly on Kestrel! Navigate to /plaintext and /json to see other endpoints."u8;

    private static async Task Index(IHttpRequestFeature req, IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = 200;
        res.Headers.ContentType = "text/plain";
        res.Headers.ContentLength = IndexPayload.Length;

        var body = features.GetResponseBodyFeature();

        await body.StartAsync();
        body.Writer.Write(IndexPayload);
        await body.Writer.FlushAsync();
        await body.CompleteAsync();
    }

    private static ReadOnlySpan<byte> HelloWorldPayload => "Hello, World!"u8;

    private static async Task Plaintext(IHttpRequestFeature req, IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = 200;
        res.Headers.ContentType = "text/plain";
        res.Headers.ContentLength = HelloWorldPayload.Length;

        var body = features.GetResponseBodyFeature();

        await body.StartAsync();
        body.Writer.Write(HelloWorldPayload);
        await body.Writer.FlushAsync();
        await body.CompleteAsync();
    }

    private static readonly JsonSerializerOptions _jsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly ObjectPoolProvider _objectPoolProvider = new DefaultObjectPoolProvider();
    private static readonly ObjectPool<ArrayBufferWriter<byte>> _bufferWriterPool = _objectPoolProvider.Create<ArrayBufferWriter<byte>>();
    private static readonly ObjectPool<Utf8JsonWriter> _jsonWriterPool = _objectPoolProvider.Create(new Utf8JsonWriterPooledObjectPolicy());

    private static async Task Json(IHttpRequestFeature req, IHttpResponseFeature res, IFeatureCollection features)
    {
        res.StatusCode = 200;
        res.Headers.ContentType = "application/json";

        //Span<byte> buffer = stackalloc byte[256];
        var bufferWriter = _bufferWriterPool.Get();
        var jsonWriter = _jsonWriterPool.Get();

        bufferWriter.ResetWrittenCount();
        jsonWriter.Reset(bufferWriter);

        JsonSerializer.Serialize(jsonWriter, new { message = "Hello, World!" }, _jsonSerializerOptions);

        res.Headers.ContentLength = bufferWriter.WrittenCount;

        var body = features.GetResponseBodyFeature();

        await body.StartAsync();
        bufferWriter.WrittenSpan.CopyTo(body.Writer.GetSpan(bufferWriter.WrittenCount));
        body.Writer.Advance(bufferWriter.WrittenCount);
        await body.Writer.FlushAsync();
        await body.CompleteAsync();

        _jsonWriterPool.Return(jsonWriter);
        _bufferWriterPool.Return(bufferWriter);
    }

    private class Utf8JsonWriterPooledObjectPolicy : IPooledObjectPolicy<Utf8JsonWriter>
    {
        private static readonly ArrayBufferWriter<byte> _dummyBufferWriter = new(256);

        public Utf8JsonWriter Create() => new(_dummyBufferWriter, new() { Indented = false, SkipValidation = true });

        public bool Return(Utf8JsonWriter obj)
        {
            //obj.Reset();
            return true;
        }
    }
}
