// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Downstream
{
    public class Program
    {
        private static readonly byte[] InvalidPayloadMessage = Encoding.UTF8.GetBytes("Invalid payload size");
        private const int MaxSize = 1_000_000; // ~ 1MB
        private static ImmutableDictionary<int, byte[]> _payloads = ImmutableDictionary<int, byte[]>.Empty;
        private const int MaxDelay = 2_000;
        
        public static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .AddCommandLine(args)
                .Build();

            new WebHostBuilder()
                .UseKestrel()
                .UseConfiguration(config)
                .Configure(app => app.Run( async (context) =>
                {
                    if (!int.TryParse(context.Request.Query["s"], out var size) || size < 0 || size > MaxSize)
                    {
                        context.Response.StatusCode = 500;
                        await context.Response.Body.WriteAsync(InvalidPayloadMessage);
                    }

                    if (!int.TryParse(context.Request.Query["d"], out var delay) || delay < 0 || delay > MaxDelay)
                    {
                        delay = 0;
                    }

                    if (!_payloads.TryGetValue(size, out var payload))
                    {
                        payload = Encoding.UTF8.GetBytes(new string('a', size));
                        _payloads = _payloads.Add(size, payload);
                    }

                    if (delay != 0)
                    {
                        await Task.Delay(delay);
                    }

                    await context.Response.Body.WriteAsync(payload);
                }))
                .Build()
                .Run();
        }
    }
}
