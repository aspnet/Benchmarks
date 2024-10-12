// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using RazorSlices;

namespace PlatformBenchmarks
{
    public partial class BenchmarkApplication
    {
#if DATABASE
        private async Task FortunesRawNoTemplate(PipeWriter pipeWriter)
        {
            await OutputFortunesRawNoTemplate(
                pipeWriter,
                // To isolate template rendering from DB access, comment out the line above and uncomment the line below
                //await RawDb.LoadFortunesRowsNoDbUtf16(),
                await DapperDb.LoadFortunesRows()
                );
        }
        private async Task FortunesRaw(PipeWriter pipeWriter)
        {
            await OutputFortunes(
                pipeWriter,
                await RawDb.LoadFortunesRows(),
                // To isolate template rendering from DB access, comment out the line above and uncomment the line below
                //await RawDb.LoadFortunesRowsNoDb(),
                FortunesTemplateFactory);
        }

        private async Task FortunesDapper(PipeWriter pipeWriter)
        {
            await OutputFortunes(pipeWriter, await DapperDb.LoadFortunesRows(), FortunesDapperTemplateFactory);
        }

        private async Task FortunesEf(PipeWriter pipeWriter)
        {
            await OutputFortunes(pipeWriter, await EfDb.LoadFortunesRows(), FortunesEfTemplateFactory);
        }

        private ValueTask OutputFortunes<TModel>(PipeWriter pipeWriter, TModel model, SliceFactory<TModel> templateFactory)
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
            var template = templateFactory(model);
            // Kestrel PipeWriter span size is 4K, headers above already written to first span & template output is ~1350 bytes,
            // so 2K chunk size should result in only a single span and chunk being used.
            var chunkedWriter = GetChunkedWriter(pipeWriter, chunkSizeHint: 2048);
            var renderTask = template.RenderAsync(chunkedWriter, null, HtmlEncoder);

            if (renderTask.IsCompletedSuccessfully)
            {
                renderTask.GetAwaiter().GetResult();
                EndTemplateRendering(chunkedWriter, template);
                return ValueTask.CompletedTask;
            }

            return AwaitTemplateRenderTask(renderTask, chunkedWriter, template);
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

        private ValueTask OutputFortunesRawNoTemplate(PipeWriter pipeWriter, List<FortuneUtf16> data)
        {
            var preamble = """
                HTTP/1.1 200 OK
                Server: K
                Content-Type: text/html; charset=utf-8
                Content-Length: 
                """u8;

            var fortunesTableStart = "<!DOCTYPE html><html><head><title>Fortunes</title></head><body><table><tr><th>id</th><th>message</th></tr>"u8;
            var fortunesRowStart = "<tr><td>"u8;
            var fortunesColumn = "</td><td>"u8;
            var fortunesRowEnd = "</td></tr>"u8;
            var fortunesTableEnd = "</table></body></html>"u8;

            var writer = GetWriter(pipeWriter, sizeHint: 1600); // in reality it's 1361

            writer.Write(preamble);

            var lengthWriter = writer;
            writer.Write(_contentLengthGap);

            // Date header
            writer.Write(DateHeader.HeaderBytes);

            var bodyStart = writer.Buffered;

            writer.Write(fortunesTableStart);
            foreach (var item in data)
            {
                writer.Write(fortunesRowStart);
                writer.WriteNumeric((uint)item.Id);
                writer.Write(fortunesColumn);
                writer.WriteUtf8String(HtmlEncoder.Encode(item.Message));
                writer.Write(fortunesRowEnd);
            }
            writer.Write(fortunesTableEnd);
            lengthWriter.WriteNumeric((uint)(writer.Buffered - bodyStart));

            writer.Commit();

            return ValueTask.CompletedTask;
        }
#endif
    }
}
