// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RazorSlices;

namespace PlatformBenchmarks
{
    public partial class BenchmarkApplication
    {
        private async Task FortunesRaw(PipeWriter pipeWriter)
        {
            await OutputFortunes(pipeWriter, await RawDb.LoadFortunesRows(), FortunesTemplateFactory);
        }

        private async Task FortunesDapper(PipeWriter pipeWriter)
        {
            await OutputFortunes(pipeWriter, await DapperDb.LoadFortunesRows(), FortunesDapperTemplateFactory);
        }

        private async Task FortunesEf(PipeWriter pipeWriter)
        {
            await OutputFortunes(pipeWriter, await EfDb.LoadFortunesRows(), FortunesEfTemplateFactory);
        }

        private ValueTask OutputFortunes<TModel>(PipeWriter pipeWriter, TModel model, SliceFactory templateFactory)
        {
            // Render headers
            var preamble = """
                HTTP/1.1 200 OK
                Server: K
                Content-Type: text/html; charset=utf-8
                Transfer-Encoding: chunked
                """u8;
            var headersLength = preamble.Length + DateHeader.HeaderBytes.Length;
            var headersSpan = pipeWriter.GetSpan(headersLength);
            preamble.CopyTo(headersSpan);
            DateHeader.HeaderBytes.CopyTo(headersSpan[preamble.Length..]);
            pipeWriter.Advance(headersLength);

            // Render body
            var template = (RazorSlice<TModel>)templateFactory();
            template.Model = model;
            // Kestrel PipeWriter span size is 4K, headers above already written to first span & template output is ~1350 bytes,
            // so 2K chunk size should result in only a single span and chunk being used.
            var chunkedWriter = GetChunkedWriter(pipeWriter, chunkSize: 2048);
            var renderTask = template.RenderAsync(chunkedWriter, null, HtmlEncoder);

            if (renderTask.IsCompletedSuccessfully)
            {
                EndTemplateRendering(chunkedWriter, template);
                return ValueTask.CompletedTask;
            }

            return AwaitTemplateRenderTask(renderTask, chunkedWriter, template);
        }

        private static ValueTask AsValueTask<T>(ValueTask<T> valueTask)
        {
            if (valueTask.IsCompletedSuccessfully)
            {
                var _ = valueTask.GetAwaiter().GetResult();
                return default;
            }

            return new ValueTask(valueTask.AsTask());
        }

        private static async ValueTask AwaitTemplateRenderTask(ValueTask renderTask, ChunkedBufferWriter<WriterAdapter> chunkedWriter, RazorSlice template)
        {
            await renderTask;
            EndTemplateRendering(chunkedWriter, template);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EndTemplateRendering(ChunkedBufferWriter<WriterAdapter> chunkedWriter, RazorSlice template)
        {
            chunkedWriter.End();
            ReturnChunkedWriter(chunkedWriter);
            template.Dispose();
        }
    }
}
