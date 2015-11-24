// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Benchmarks.Data;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Benchmarks
{
    public class MultipleQueriesRawMiddleware
    {
        private static readonly PathString _path = new PathString("/queries/raw");
        private static readonly Random _random = new Random();
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private readonly RequestDelegate _next;
        private readonly string _connectionString;
        private readonly DbProviderFactory _dbProviderFactory;

        public MultipleQueriesRawMiddleware(RequestDelegate next, string connectionString, DbProviderFactory dbProviderFactory)
        {
            _next = next;
            _connectionString = connectionString;
            _dbProviderFactory = dbProviderFactory;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            // We check Ordinal explicitly first because it's faster than OrdinalIgnoreCase
            if (httpContext.Request.Path.StartsWithSegments(_path, StringComparison.Ordinal) ||
                httpContext.Request.Path.StartsWithSegments(_path, StringComparison.OrdinalIgnoreCase))
            {
                var count = GetQueryCount(httpContext);
                var rows = await LoadRows(count, _connectionString, _dbProviderFactory);

                var result = JsonConvert.SerializeObject(rows, _jsonSettings);

                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "application/json";
                httpContext.Response.ContentLength = result.Length;

                await httpContext.Response.WriteAsync(result);

                return;
            }

            await _next(httpContext);
        }

        private static int GetQueryCount(HttpContext httpContext)
        {
            var queries = 1;
            var queriesRaw = httpContext.Request.Query["queries"];

            if (queriesRaw.Count == 1)
            {
                int.TryParse(queriesRaw, out queries);
            }

            return queries > 500
                ? 500
                : queries > 0
                    ? queries
                    : 1;
        }

        private static async Task<World[]> LoadRows(int count, string connectionString, DbProviderFactory dbProviderFactory)
        {
            var result = new World[count];

            using (var db = dbProviderFactory.CreateConnection())
            using (var cmd = db.CreateCommand())
            {
                db.ConnectionString = connectionString;
                await db.OpenAsync();

                cmd.CommandText = "SELECT [Id], [RandomNumber] FROM [World] WHERE [Id] = @Id";
                var id = cmd.CreateParameter();
                id.ParameterName = "@Id";
                id.DbType = DbType.Int32;
                cmd.Parameters.Add(id);

                for (int i = 0; i < count; i++)
                {
                    id.Value = _random.Next(1, 10001);
                    using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        await rdr.ReadAsync();

                        result[i] = new World
                        {
                            Id = rdr.GetInt32(0),
                            RandomNumber = rdr.GetInt32(1)
                        };
                    }
                }

                db.Close();
            }

            return result;
        }
    }

    public static class MultipleQueriesRawMiddlewareExtensions
    {
        public static IApplicationBuilder UseMultipleQueriesRaw(this IApplicationBuilder builder, string connectionString)
        {
            return builder.UseMiddleware<MultipleQueriesRawMiddleware>(connectionString);
        }
    }
}
