// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;

namespace Benchmarks
{
    public class FortunesRawMiddleware
    {
        private static readonly PathString _path = new PathString("/fortunes/raw");

        private readonly RequestDelegate _next;
        private readonly string _connectionString;

        public FortunesRawMiddleware(RequestDelegate next, string connectionString)
        {
            _next = next;
            _connectionString = connectionString;
        }

        public async Task Invoke(HttpContext httpContext, HtmlEncoder htmlEncoder)
        {
            // We check Ordinal explicitly first because it's faster than OrdinalIgnoreCase
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal) ||
                httpContext.Request.Path.StartsWithSegments(_path, StringComparison.OrdinalIgnoreCase))
            {
                var rows = await LoadRows(_connectionString);

                await RenderHtml(rows, httpContext, htmlEncoder);

                return;
            }

            await _next(httpContext);
        }

        private static async Task<IEnumerable<Fortune>> LoadRows(string connectionString)
        {
            var result = new List<Fortune>();

            using (var db = new SqlConnection(connectionString))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT [Id], [Message] FROM [Fortune]";

                await db.OpenAsync();
                using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                {
                    while (await rdr.ReadAsync())
                    {
                        result.Add(new Fortune
                        {
                            Id = rdr.GetInt32(0),
                            Message = rdr.GetString(1)
                        });
                    }
                }
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
    
    public static class FortunesRawMiddlewareExtensions
    {
        public static IApplicationBuilder UseFortunesRaw(this IApplicationBuilder builder, string connectionString)
        {
            return builder.UseMiddleware<FortunesRawMiddleware>(connectionString);
        }
    }
}
