// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Net;
using System.Text;

namespace BenchmarkServer
{
    public class Startup
    {
        private readonly IConfiguration _config;

        public Startup(IConfiguration configuration)
        {
            _config = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRouting();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseWebSockets();
            app.Use(async (context, next) =>
                {
                    if (context.Request.Path == "/Echo")
                    {
                        if (context.WebSockets.IsWebSocketRequest)
                        {
                            using (WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync())
                            {
                                await Echo(context, webSocket);
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

        private async Task Echo(HttpContext context, WebSocket webSocket)
        {
            var receivedMessage = "";
            var buffer = new byte[1024 * 4];
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            while (true)
            {
                if (result.CloseStatus.HasValue)
                {
                    break;
                }

                receivedMessage += Encoding.UTF8.GetString(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    await webSocket.SendAsync(Encoding.UTF8.GetBytes(receivedMessage).AsMemory(), result.MessageType, result.EndOfMessage, CancellationToken.None);
                    receivedMessage = "";
                }

                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            }

            await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
        }
    }
}
