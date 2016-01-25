// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Benchmarks.Data;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Benchmarks
{
    public class FortunesDapperMiddleware
    {
        private static readonly PathString _path = new PathString("/fortunes/dapper");

        private readonly RequestDelegate _next;
        private readonly string _connectionString;
        private readonly DbProviderFactory _dbProviderFactory;

        public FortunesDapperMiddleware(RequestDelegate next, string connectionString, DbProviderFactory dbProviderFactory)
        {
            _next = next;
            _connectionString = connectionString;
            _dbProviderFactory = dbProviderFactory;
        }

        public async Task Invoke(HttpContext httpContext, HtmlEncoder htmlEncoder)
        {
            // We check Ordinal explicitly first because it's faster than OrdinalIgnoreCase
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal) ||
                httpContext.Request.Path.StartsWithSegments(_path, StringComparison.OrdinalIgnoreCase))
            {
                var rows = await LoadRows(_connectionString, _dbProviderFactory);

                await RenderHtml(rows, httpContext, htmlEncoder);

                return;
            }

            await _next(httpContext);
        }

        private static async Task<IEnumerable<Fortune>> LoadRows(string connectionString, DbProviderFactory dbProviderFactory)
        {
            List<Fortune> result;

            using (var db = dbProviderFactory.CreateConnection())
            {
                db.ConnectionString = connectionString;
                // note: don't need to open connection if only doing one thing; let dapper do it
                result = (await db.QueryAsync<Fortune>("SELECT [Id], [Message] FROM [Fortune]")).AsList();
            }

            result.Add(new Fortune { Message = "Additional fortune added at request time." });
            result.Sort();

            return result;
        }

        private static async Task RenderHtml(IEnumerable<Fortune> model, HttpContext httpContext, HtmlEncoder htmlEncoder)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/html; charset=UTF-8";

            await httpContext.Response.WriteAsync("<!DOCTYPE html><html><head><title>Fortunes</title></head><body><table><tr><th>id</th><th>message</th></tr>");

            foreach (var item in model)
            {
                await httpContext.Response.WriteAsync(
                    $"<tr><td>{htmlEncoder.Encode(item.Id.ToString())}</td><td>{htmlEncoder.Encode(item.Message)}</td></tr>");
            }

            await httpContext.Response.WriteAsync("</table></body></html>");
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
