// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Benchmarks
{
    public class SingleQueryRawMiddleware
    {
        private static readonly PathString _path = new PathString("/db/raw");
        private static readonly Random _random = new Random();
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

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
                var row = await LoadRow(_connectionString);

                var result = JsonConvert.SerializeObject(row, _jsonSettings);

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "application/json";
                httpContext.Response.ContentLength = result.Length;

                await httpContext.Response.WriteAsync(result);

                return;
            }

            await _next(httpContext);
        }

        private static async Task<World> LoadRow(string connectionString)
        {
            using (var db = new SqlConnection(connectionString))
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT [Id], [RandomNumber] FROM [World] WHERE [Id] = @Id";
                cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = _random.Next(1, 10001) });

                await db.OpenAsync();
                using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                {
                    await rdr.ReadAsync();

                    return new World
                    {
                        Id = rdr.GetInt32(0),
                        RandomNumber = rdr.GetInt32(1)
                    };
                }
            }
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
