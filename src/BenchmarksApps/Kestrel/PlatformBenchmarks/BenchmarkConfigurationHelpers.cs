// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.IO.Pipelines;
using System.Runtime.InteropServices;

namespace PlatformBenchmarks
{
    public static class BenchmarkConfigurationHelpers
    {
        public static IWebHostBuilder UseBenchmarksConfiguration(this IWebHostBuilder builder, IConfiguration configuration)
        {
            builder.UseConfiguration(configuration);

            // Handle the transport type
            var webHost = builder.GetSetting("KestrelTransport");

            Console.WriteLine($"Transport: {webHost}");

            builder.UseSockets(options =>
            {
                // if (int.TryParse(builder.GetSetting("threadCount"), out int threadCount))
                // {
                //     options.IOQueueCount = threadCount;
                // }

                // options.IOQueueCount = 0;

                if (Environment.GetEnvironmentVariable("DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS") != "1")
                    throw new Exception(
                        "Please set exported env vars 'DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS' and 'DOTNET_SYSTEM_NET_SOCKETS_THREAD_COUNT' to 1 or logical core count ");

#if NETCOREAPP5_0 || NET5_0 || NET6_0_OR_GREATER
                options.WaitForDataBeforeAllocatingBuffer = false;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // options.UnsafePreferInlineScheduling = true;
                    // options.UnsafePreferInlineScheduling = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS") == "1"; 
                }

                Console.WriteLine($"Options: WaitForData={options.WaitForDataBeforeAllocatingBuffer}, PreferInlineScheduling={options.UnsafePreferInlineScheduling}, IOQueue={options.IOQueueCount}");
#endif
            });

            return builder;
        }

        public static IPEndPoint CreateIPEndPoint(this IConfiguration config)
        {
            var url = config["server.urls"] ?? config["urls"];

            if (string.IsNullOrEmpty(url))
            {
                return new IPEndPoint(IPAddress.Loopback, 8080);
            }

            var address = BindingAddress.Parse(url);

            IPAddress ip;

            if (string.Equals(address.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                ip = IPAddress.Loopback;
            }
            else if (!IPAddress.TryParse(address.Host, out ip))
            {
                ip = IPAddress.IPv6Any;
            }

            return new IPEndPoint(ip, address.Port);
        }
    }
}
