// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Configuration;

namespace Proxy
{
    public class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly HttpClientPool _httpClientPool = new HttpClientPool(Environment.ProcessorCount * 2);

        private static string _scheme;
        private static HostString _host;
        private static string _pathBase;
        private static QueryString _appendQuery;

        public static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .AddCommandLine(args)
                .Build();

            // The url all requests will be forwarded to
            var baseUriArg = config["baseUri"];
            
            if (String.IsNullOrWhiteSpace(baseUriArg))
            {
                throw new ArgumentException("--baseUri is required");
            }

            var baseUri = new Uri(baseUriArg);

            // Cache base URI values
            _scheme = baseUri.Scheme;
            _host = new HostString(baseUri.Authority);
            _pathBase = baseUri.AbsolutePath;
            _appendQuery = new QueryString(baseUri.Query);

            Console.WriteLine($"Base URI: {baseUriArg}");

            // The number of outbound requests to send per inbound request
            if (!int.TryParse(config["concurrency"], out var concurrency) || concurrency == 0)
            {
                concurrency = 1;
            }

            Console.WriteLine($"Concurrency level: {concurrency}");

            // Whether to pool HttpClient instances
            if (!bool.TryParse(config["pool"], out var pool))
            {
                pool = false;
            }

            Console.WriteLine($"Pool HttpClient instances: {pool}");

            _httpClientPool.BaseAddress = _httpClient.BaseAddress;

            var builder = new WebHostBuilder()
                .UseKestrel()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseConfiguration(config);

            if (pool)
            {
                builder = builder.Configure(app => app.Run(async (context) =>
                {
                    var destinationUri = BuildDestinationUri(context);

                    var tasks = new Task<HttpResponseMessage>[concurrency];

                    for (var i = 0; i < concurrency; i++)
                    {
                        var httpClient = _httpClientPool.GetInstance();

                        try
                        {
                            using (var requestMessage = context.CreateProxyHttpRequest(destinationUri))
                            {
                                tasks[i] = httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
                            }
                        }
                        finally
                        {
                            if (pool)
                            {
                                _httpClientPool.ReturnInstance(httpClient);
                            }
                        }
                    }

                    await Task.WhenAll(tasks);

                    await context.CopyProxyHttpResponse(tasks[0].Result);

                }));
            }
            else
            {
                // Optimized path when no pooling and concurrency is 1, which is the recommended scenario for customers
                if (concurrency == 1)
                {
                    builder = builder.Configure(app => app.Run(async (context) =>
                    {
                        var destinationUri = BuildDestinationUri(context);

                        using (var requestMessage = context.CreateProxyHttpRequest(destinationUri))
                        {
                            using (var responseMessage = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted))
                            {
                                await context.CopyProxyHttpResponse(responseMessage);
                            }
                        }
                    }));
                }
                else
                {
                    builder = builder.Configure(app => app.Run(async (context) =>
                    {
                        var destinationUri = BuildDestinationUri(context);

                        var tasks = new Task<HttpResponseMessage>[concurrency];

                        for (var i = 0; i < concurrency; i++)
                        {
                            using (var requestMessage = context.CreateProxyHttpRequest(destinationUri))
                            {
                                tasks[i] = _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
                            }

                            await Task.WhenAll(tasks);

                            await context.CopyProxyHttpResponse(tasks[0].Result);
                        }
                    }));
                }
            }

            builder
                .Build()
                .Run();
        }

        private static Uri BuildDestinationUri(HttpContext context) => new Uri(UriHelper.BuildAbsolute(_scheme, _host, _pathBase, context.Request.Path, context.Request.QueryString.Add(_appendQuery)));

    }
}