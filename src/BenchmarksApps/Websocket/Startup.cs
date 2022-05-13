// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BenchmarkServer
{
    public class Startup
    {
        public void Configure(IApplicationBuilder app)
        {
            app.UseWebSockets();
            app.Use(async (context, next) =>
                {
                    if (context.Request.Path == "/Echo")
                    {
                        if (context.WebSockets.IsWebSocketRequest)
                        {
                            using (WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync(
                                new WebSocketAcceptContext()
                                {
#if USECOMPRESSION
                                    DangerousEnableCompression = true,
#endif
                                }))
                            {
                                await Echo(webSocket);
                            }
                        }
                        else
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        }
                    }
                    else
                    {
                        await next();
                    }
                });
        }

        private async Task Echo(WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            var result = await webSocket.ReceiveAsync(buffer.AsMemory(), default);
            while (result.MessageType != WebSocketMessageType.Close)
            {
                await webSocket.SendAsync(buffer.AsMemory(..result.Count), result.MessageType, result.EndOfMessage, default);
                result = await webSocket.ReceiveAsync(buffer.AsMemory(), default);
            }
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", default);
        }
    }
}
