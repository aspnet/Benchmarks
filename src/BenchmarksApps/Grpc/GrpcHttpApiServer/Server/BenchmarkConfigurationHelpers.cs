// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Server
{
    public static class BenchmarkConfigurationHelpers
    {
        public static BindingAddress CreateBindingAddress(this IConfiguration config)
        {
            var url = config["server.urls"] ?? config["urls"];

            if (string.IsNullOrEmpty(url))
            {
                return BindingAddress.Parse("http://localhost:5000");
            }

            return BindingAddress.Parse(url);
        }

        public static IPEndPoint CreateIPEndPoint(this IConfiguration config)
        {
            var address = config.CreateBindingAddress();

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
