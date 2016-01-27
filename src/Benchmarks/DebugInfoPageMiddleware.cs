// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.PlatformAbstractions;

namespace Benchmarks
{
    public class DebugInfoPageMiddleware
    {
        private static readonly PathString _path = new PathString("/debug");
#if DEBUG
        private static readonly string _configurationName = "Debug";
#elif RELEASE
        private static readonly string _configurationName = "Release";
#else
        private static readonly string _configurationName = "";
#endif

        private readonly IApplicationEnvironment _appEnv;
        private readonly IHostingEnvironment _hostingEnv;
        private readonly RequestDelegate _next;

        public DebugInfoPageMiddleware(RequestDelegate next, IHostingEnvironment hostingEnv, IApplicationEnvironment appEnv)
        {
            _next = next;
            _hostingEnv = hostingEnv;
            _appEnv = appEnv;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                httpContext.Response.ContentType = "text/html";

                await httpContext.Response.WriteAsync("<h1>Application Information</h1>");
                await httpContext.Response.WriteAsync("<ul>");

                await httpContext.Response.WriteAsync($"<li>Environment: {_hostingEnv.EnvironmentName}</li>");
                await httpContext.Response.WriteAsync($"<li>Framework: {_appEnv.RuntimeFramework.FullName}</li>");
                await httpContext.Response.WriteAsync($"<li>Configuration: {_configurationName}</li>");
                await httpContext.Response.WriteAsync($"<li>Server: {_hostingEnv.Configuration["server"]}</li>");
                await httpContext.Response.WriteAsync($"<li>Server URLs: {_hostingEnv.Configuration["server.urls"]}</li>");
                await httpContext.Response.WriteAsync($"<li>Supports Send File: {httpContext.Features.Get<IHttpSendFileFeature>() != null}</li>");

                await httpContext.Response.WriteAsync($"<li>Server features:<ul>");
                
                foreach (var feature in httpContext.Features)
                {
                    await httpContext.Response.WriteAsync($"<li>{feature.Key.Name}</li>");
                }
                await httpContext.Response.WriteAsync($"</ul></li>");

                await httpContext.Response.WriteAsync("</ul>");
                return;
            }

            await _next(httpContext);
        }
    }

    public static class DebugInfoPageMiddlewareExtensions
    {
        public static IApplicationBuilder UseDebugInfoPage(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<DebugInfoPageMiddleware>();
        }
    }
}
