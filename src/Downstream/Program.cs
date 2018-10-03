// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Downstream
{
    public class Program
    {
        // We only handle powers of 2 long results, up to 2^15 = 32KB
        private const int MaxSize = 16;
        private static readonly byte[][] _responses;
        private static readonly int[] _responseLengths;
        private const int MaxDelay = 2_000;

        static Program()
        {
            _responses = new byte[MaxSize][];
            _responseLengths = new int[MaxSize];

            for (var i = 0; i < MaxSize; i++)
            {
                var length = (int)Math.Pow(2, i);
                _responses[i] = Encoding.UTF8.GetBytes(new string('a', length));
                _responseLengths[i] = length;
            }         
        }

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
                    if (!int.TryParse(context.Request.Query["s"], out var size) || size < 0 || size >= MaxSize)
                    {
                        // Default to 1KB
                        size = 10;
                    }

                    if (!int.TryParse(context.Request.Query["d"], out var delay) || delay < 0 || delay >= MaxDelay)
                    {
                        delay = 0;
                    }

                    var _response = _responses[size];
                    var _length = _responseLengths[size];

                    if (delay != 0)
                    {
                        await Task.Delay(delay);
                    }

                    await context.Response.Body.WriteAsync(_response, 0, _length);
                }))
                .Build()
                .Run();
        }
    }
}
