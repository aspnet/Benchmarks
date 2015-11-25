// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;
using Microsoft.Data.Entity;

namespace Benchmarks
{
    public class FortunesEfMiddleware
    {
        private static readonly PathString _path = new PathString("/fortunes/ef");

        private readonly RequestDelegate _next;

        public FortunesEfMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext, HtmlEncoder htmlEncoder)
        {
            // We check Ordinal explicitly first because it's faster than OrdinalIgnoreCase
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal) ||
                httpContext.Request.Path.StartsWithSegments(_path, StringComparison.OrdinalIgnoreCase))
            {
                var db = (ApplicationDbContext)httpContext.RequestServices.GetService(typeof(ApplicationDbContext));
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

                var rows = await LoadRows(db);

                await RenderHtml(rows, httpContext, htmlEncoder);

                return;
            }

            await _next(httpContext);
        }

        private static async Task<IEnumerable<Fortune>> LoadRows(ApplicationDbContext dbContext)
        {
            var result = new List<Fortune>();

            result.AddRange(await dbContext.Fortune.ToListAsync());

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
    
    public static class FortunesEfMiddlewareExtensions
    {
        public static IApplicationBuilder UseFortunesEf(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<FortunesEfMiddleware>();
        }
    }
}
