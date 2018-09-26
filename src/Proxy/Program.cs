// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Proxy
{
    public class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly HttpClientPool _httpClientPool = new HttpClientPool(Environment.ProcessorCount * 2);

        public static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .AddCommandLine(args)
                .Build();

            // The url all requests will be forwarded to
            var downstream = config["downstream"];
            
            if (String.IsNullOrWhiteSpace(downstream))
            {
                throw new ArgumentException("--downstream is required");
            }

            Console.WriteLine($"Downstream: {downstream}");

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

            _httpClient.BaseAddress = new Uri(downstream);
            _httpClientPool.BaseAddress = _httpClient.BaseAddress;

            new WebHostBuilder()
                .UseKestrel()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseConfiguration(config)
                .Configure(app => app.Run(async (context) =>
                {
                    var path = context.Request.Path.Value;

                    var tasks = new Task<string>[concurrency];
                    for (var i = 0; i < concurrency; i++)
                    {
                        var httpClient = pool ? _httpClientPool.GetInstance() : _httpClient;
                        try
                        {
                            tasks[i] = httpClient.GetStringAsync(path);
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

                    await context.Response.WriteAsync(await tasks[0]);
                }))
                .Build()
                .Run();
        }
    }

}