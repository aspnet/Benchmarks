// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Downstream
{
    public class Program
    {
        private const int MaxSize = 1_000_000; // ~ 1MB
        private const int MaxDelay = 2_000;

        private static ImmutableDictionary<int, byte[]> _payloads = ImmutableDictionary<int, byte[]>.Empty;
        
        private static Dictionary<string, string> _headers = new Dictionary<string, string>();
        private static Dictionary<string, string> _cookies = new Dictionary<string, string>();
        private static int _size = 10;
        private static int _delay = 0;
        
        public static void Main(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var arg = FormatArg(i);
                var nextArg = FormatArg(i+1);

                switch (arg)
                {
                    case "-d" :
                    case "--delay" : int.TryParse(nextArg, out _delay); i++; break;
                    case "-s" :
                    case "--size" : int.TryParse(nextArg, out _size); i++; break;
                    case "-h":
                    case "--headers" : var values = nextArg.Split(':', 2); _headers[values[0]] = values[1]; i++; break;
                    case "-c":
                    case "--cookies" : values = nextArg.Split(':', 2); _cookies[values[0]] = values[1]; i++; break;
                }

                string FormatArg(int index)
                {
                    if (index >= args.Length)
                    {
                        return null;
                    }

                    return args[index].Trim();
                }
            }

            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webHostBuilder => webHostBuilder
                .ConfigureKestrel((context, kestrelOptions) =>
                {
                    kestrelOptions.ConfigureHttpsDefaults(httpsOptions =>
                    {
                        httpsOptions.ServerCertificate = new X509Certificate2(Path.Combine(context.HostingEnvironment.ContentRootPath, "testCert.pfx"), "testPassword");
                    });
                })
                .Configure(app => app.Run(async context =>
                {
                    var size = _size;
                    var delay = _delay;

                    // These query string parameters are kept for backward compatibility
                    if (context.Request.Query.ContainsKey("s") && int.TryParse(context.Request.Query["s"], out size))
                    {
                        size = Math.Max(0, size);
                        size = Math.Min(MaxSize, size);
                    }

                    if (context.Request.Query.ContainsKey("d") && int.TryParse(context.Request.Query["d"], out delay))
                    {
                        delay = Math.Max(0, delay);
                        delay = Math.Min(MaxDelay, delay);
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

                    foreach (var header in _headers)
                    {
                        context.Response.Headers.Add(header.Key, header.Value);
                    }
                    
                    foreach (var cookie in _cookies)
                    {
                        context.Response.Cookies.Append(cookie.Key, cookie.Value);
                    }
                    
                    await context.Response.Body.WriteAsync(payload);
                })))
                .Build()
                .Run();
        }
    }
}
