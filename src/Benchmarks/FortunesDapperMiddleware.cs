// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Data.Common;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Benchmarks
{
    public class FortunesDapperMiddleware
    {
        private static readonly PathString _path = new PathString(Scenarios.GetPaths(s => s.DbFortunesDapper)[0]);

        private readonly RequestDelegate _next;
        private readonly string _connectionString;
        private readonly DbProviderFactory _dbProviderFactory;
        private readonly HtmlEncoder _htmlEncoder;

        public FortunesDapperMiddleware(
            RequestDelegate next,
            string connectionString,
            DbProviderFactory dbProviderFactory,
            HtmlEncoder htmlEncoder)
        {
            _next = next;
            _connectionString = connectionString;
            _dbProviderFactory = dbProviderFactory;
            _htmlEncoder = htmlEncoder;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal))
            {
                var rows = await DapperDb.LoadFortunesRows(_connectionString, _dbProviderFactory);

                await MiddlewareHelpers.RenderFortunesHtml(rows, httpContext, _htmlEncoder);

                return;
            }

            await _next(httpContext);
        }
    }
    
    public static class FortunesDapperMiddlewareExtensions
    {
        public static IApplicationBuilder UseFortunesDapper(this IApplicationBuilder builder, string connectionString)
        {
            return builder.UseMiddleware<FortunesDapperMiddleware>(connectionString);
        }
    }
}
