// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;

namespace PlatformBenchmarks
{
    public static class BenchmarkConfigurationHelpers
    {
        public static IWebHostBuilder UseBenchmarksConfiguration(this IWebHostBuilder builder, IConfiguration configuration)
        {
            builder.UseConfiguration(configuration);

            // Handle the transport type
            var webHost = builder.GetSetting("KestrelTransport");

            Console.WriteLine($"KestrelTransport={webHost}");

            if (string.Equals(webHost, "Sockets", StringComparison.OrdinalIgnoreCase))
            {
                builder.UseSockets(options =>
                {
                    if (int.TryParse(builder.GetSetting("threadCount"), out int threadCount))
                    {
                       options.IOQueueCount = threadCount;
                    }
#if NETCOREAPP5_0
                    typeof(SocketTransportOptions).GetProperty("WaitForDataBeforeAllocatingBuffer")?.SetValue(options, false);
#endif
                });
            }
            else if (string.Equals(webHost, "LinuxTransport", StringComparison.OrdinalIgnoreCase))
            {
                builder.UseLinuxTransport(options =>
                {
                    options.ApplicationSchedulingMode = PipeScheduler.Inline;
                });
            }
            else if(string.Equals(webHost, "Kestrel", StringComparison.OrdinalIgnoreCase))
            {
                builder.UseKestrel();
            }
            else // use the fastest transport by default
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IConnectionListenerFactory, SocketPipeTransportFactory>();

                    services.AddTransient<IConfigureOptions<KestrelServerOptions>, KestrelServerOptionsSetup>();
                    services.AddSingleton<IServer, KestrelServer>();
                });
            }

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
