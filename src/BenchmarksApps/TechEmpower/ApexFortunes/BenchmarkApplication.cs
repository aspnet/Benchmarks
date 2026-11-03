using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using ApexFortunes;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.Extensions.ObjectPool;
using RazorSlices;

namespace PlatformBenchmarks;

public sealed partial class BenchmarkApplication
{
    private static readonly DefaultObjectPool<ChunkedPipeWriter> ChunkedWriterPool =
        new(new ChunkedWriterObjectPolicy());

    private RequestType _requestType;

    internal static FortuneDatabase Database { get; set; } = null!;

    public void OnStartLine(
        HttpVersionAndMethod versionAndMethod,
        TargetOffsetPathLength targetPath,
        Span<byte> startLine)
    {
        _requestType = versionAndMethod.Method ==
            Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpMethod.Get &&
            startLine.Slice(targetPath.Offset, targetPath.Length).SequenceEqual("/fortunes"u8)
                ? RequestType.Fortunes
                : RequestType.NotRecognized;
    }

    private Task ProcessRequestAsync() => _requestType switch
    {
        RequestType.Fortunes => RenderDatabaseAsync(),
        _ => OutputEmptyAsync(Writer),
    };

    private async Task RenderDatabaseAsync() =>
        await OutputFortunesAsync(Writer, await Database.LoadAsync());

    private ValueTask OutputFortunesAsync(
        PipeWriter pipeWriter,
        List<Fortune> fortunes)
    {
        var template = ApexFortunes.Templates.Fortunes.Create(fortunes);
        var chunkedWriter = StartResponse(pipeWriter);
        var renderTask = template.RenderAsync(chunkedWriter, HtmlEncoder);
        if (renderTask.IsCompletedSuccessfully)
        {
            renderTask.GetAwaiter().GetResult();
            EndTemplateRendering(chunkedWriter, template);
            return ValueTask.CompletedTask;
        }

        return AwaitTemplateRenderTask(renderTask, chunkedWriter, template);
    }

    private static Task OutputEmptyAsync(PipeWriter pipeWriter)
    {
        var writer = StartResponse(pipeWriter);
        writer.Complete();
        ReturnChunkedWriter(writer);
        return Task.CompletedTask;
    }

    private static ChunkedPipeWriter StartResponse(PipeWriter pipeWriter)
    {
        var preamble =
            "HTTP/1.1 200 OK\r\nServer: K\r\nContent-Type: text/html; charset=utf-8\r\nTransfer-Encoding: chunked"u8;
        var headersLength = preamble.Length + DateHeader.HeaderBytes.Length;
        var headersSpan = pipeWriter.GetSpan(headersLength);
        preamble.CopyTo(headersSpan);
        DateHeader.HeaderBytes.CopyTo(headersSpan[preamble.Length..]);
        pipeWriter.Advance(headersLength);

        var writer = ChunkedWriterPool.Get();
        writer.SetOutput(pipeWriter, 2048);
        return writer;
    }

    private static async ValueTask AwaitTemplateRenderTask(
        ValueTask renderTask,
        ChunkedPipeWriter chunkedWriter,
        RazorSlice template)
    {
        await renderTask;
        EndTemplateRendering(chunkedWriter, template);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EndTemplateRendering(
        ChunkedPipeWriter chunkedWriter,
        RazorSlice template)
    {
        chunkedWriter.Complete();
        ReturnChunkedWriter(chunkedWriter);
        template.Dispose();
    }

    private sealed class ChunkedWriterObjectPolicy :
        IPooledObjectPolicy<ChunkedPipeWriter>
    {
        public ChunkedPipeWriter Create() => new();

        public bool Return(ChunkedPipeWriter writer)
        {
            writer.Reset();
            return true;
        }
    }

    private enum RequestType
    {
        NotRecognized,
        Fortunes,
    }
}
