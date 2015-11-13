// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;
using Newtonsoft.Json;

namespace Benchmarks
{
    public class SingleQueryRawMiddleware
    {
        private static readonly PathString _path = new PathString("/db/raw");
        private static readonly Random _random = new Random();

        private readonly RequestDelegate _next;
        private readonly string _connectionString;

        public SingleQueryRawMiddleware(RequestDelegate next, string connectionString)
        {
            _next = next;
            _connectionString = connectionString;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            // We check Ordinal explicitly first because it's faster than OrdinalIgnoreCase
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal) ||
                httpContext.Request.Path.StartsWithSegments(_path, StringComparison.OrdinalIgnoreCase))
            {
                var row = await LoadRow();

                var result = JsonConvert.SerializeObject(row);

                httpContext.Response.StatusCode = 200;
                httpContext.Response.ContentType = "application/json";
                httpContext.Response.ContentLength = result.Length;

                await httpContext.Response.WriteAsync(result);

                return;
            }

            await _next(httpContext);
        }

        private async Task<World> LoadRow()
        {
            var row = new World();

            using (var db = new SqlConnection(_connectionString))
            using (var cmd = db.CreateCommand())
            {
                var id = _random.Next(1, 10001);
                cmd.CommandText = "SELECT [Id], [RandomNumber] FROM [World] WHERE [Id] = @Id";
                cmd.Parameters.Add(new SqlParameter("@Id", System.Data.SqlDbType.Int) { Value = id });

                await db.OpenAsync();
                var rdr = await cmd.ExecuteReaderAsync();
                await rdr.ReadAsync();

                row.Id = rdr.GetInt32(0);
                row.RandomNumber = rdr.GetInt32(1);

                db.Close();
            }

            return row;
        }
    }

    public static class SingleQueryRawMiddlewareExtensions
    {
        public static IApplicationBuilder UseSingleQueryRaw(this IApplicationBuilder builder, string connectionString)
        {
            return builder.UseMiddleware<SingleQueryRawMiddleware>(connectionString);
        }
    }
}
