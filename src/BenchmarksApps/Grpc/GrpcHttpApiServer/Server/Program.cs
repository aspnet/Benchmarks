// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("hosting.json", optional: true)
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .AddCommandLine(args)
                .Build();

            return Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .ConfigureKestrel((context, options) =>
                        {
                            var endPoint = config.CreateIPEndPoint();

                            // ListenAnyIP will work with IPv4 and IPv6.
                            // Chosen over Listen+IPAddress.Loopback, which would have a 2 second delay when
                            // creating a connection on a local Windows machine.
                            options.ListenAnyIP(endPoint.Port, listenOptions =>
                            {
                                var protocol = config["protocol"] ?? "";

                                Console.WriteLine($"Protocol: {protocol}");

                                if (protocol.Equals("h2", StringComparison.OrdinalIgnoreCase))
                                {
                                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;

                                    var basePath = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                                    var certPath = Path.Combine(basePath!, "Certs/testCert.pfx");
                                    Console.WriteLine("Loading certificate from " + certPath);
                                    listenOptions.UseHttps(certPath, "testPassword");
                                }
                                else if (protocol.Equals("h2c", StringComparison.OrdinalIgnoreCase))
                                {
                                    listenOptions.Protocols = HttpProtocols.Http2;
                                }
                                else if (protocol.Equals("http", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Nothing
                                }
                                else if (protocol.Equals("https", StringComparison.OrdinalIgnoreCase))
                                {
                                    var basePath = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                                    var certPath = Path.Combine(basePath!, "Certs/testCert.pfx");
                                    listenOptions.UseHttps(certPath, "testPassword");
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Unexpected protocol: {protocol}");
                                }
                            });
                        })
                        .UseStartup<Startup>();
                })
                .ConfigureLogging(loggerFactory =>
                {
                    loggerFactory.ClearProviders();

                    if (Enum.TryParse<LogLevel>(config["LogLevel"], out var logLevel))
                    {
                        Console.WriteLine($"Console Logging enabled with level '{logLevel}'");
                        loggerFactory.AddConsole(o => o.TimestampFormat = "ss.ffff ").SetMinimumLevel(logLevel);
                    }
                })
                .UseDefaultServiceProvider((context, options) =>
                {
                    options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
                });
        }
    }
}
