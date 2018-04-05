// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime;
using System.Threading.Tasks;
using Benchmarks.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Benchmarks.Middleware
{
    public class DebugInfoPageMiddleware
    {
#if DEBUG
        private static readonly string _configurationName = "Debug";
#elif RELEASE
        private static readonly string _configurationName = "Release";
#else
        private static readonly string _configurationName = "";
#endif

        private static readonly string _targetFrameworkName = AppContext.TargetFrameworkName;

        private readonly IHostingEnvironment _hostingEnv;
        private readonly RequestDelegate _next;
        private readonly Scenarios _scenarios;
        private readonly IServerAddressesFeature _serverAddresses;

        public DebugInfoPageMiddleware(RequestDelegate next, IServerAddressesFeature serverAddresses, IHostingEnvironment hostingEnv, Scenarios scenarios)
        {
            _next = next;
            _hostingEnv = hostingEnv;
            _scenarios = scenarios;
            _serverAddresses = serverAddresses;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "text/plain";

            // If the diagnostics were explicitly requested we return 200 OK
            if (httpContext.Request.Path == "/diagnostics")
            {
                httpContext.Response.StatusCode = StatusCodes.Status200OK;

                await WriteLineAsync("ASP.NET Core Benchmarks");
                await WriteLineAsync("Configuration Information");
                await WriteLineAsync($"Environment: {_hostingEnv.EnvironmentName}");
                await WriteLineAsync($"Framework: {_targetFrameworkName}");
                await WriteLineAsync($"Server GC enabled: {GCSettings.IsServerGC}");
                await WriteLineAsync($"Configuration: {_configurationName}");
                await WriteLineAsync($"Server: {Program.Server}");
                await WriteLineAsync($"Server URLs: {string.Join(", ", _serverAddresses.Addresses)}");
                await WriteLineAsync($"Supports Send File: {httpContext.Features.Get<IHttpSendFileFeature>() != null}");
                await WriteLineAsync("");

                await WriteLineAsync($"Environment variables:");
                foreach (DictionaryEntry ev in Environment.GetEnvironmentVariables())
                {
                    await WriteLineAsync($"{ev.Key}={ev.Value}");
                }
                await WriteLineAsync("");

                await WriteLineAsync($"Server features:");
                foreach (var feature in httpContext.Features)
                {
                    await WriteLineAsync(feature.Key.Name);
                }
                await WriteLineAsync("");

                await WriteLineAsync($"Enabled scenarios:");
                foreach (var scenario in _scenarios.GetEnabled())
                {
                    await WriteLineAsync($"{scenario.Name}");
                }
                await WriteLineAsync("");

                await WriteLineAsync($"Loaded assemblies:");

                var hasAttribute = false;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    await WriteAsync(assembly.GetName().ToString());
                    await WriteLineAsync(assembly.GetName().Version.ToString());

                    var informationalVersionAttribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

                    if (informationalVersionAttribute != null)
                    {
                        await WriteAsync(informationalVersionAttribute.InformationalVersion + ";");
                        hasAttribute = true;
                    }

                    foreach (var metadataAttribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
                    {
                        await WriteAsync($"{metadataAttribute.Key}: {metadataAttribute.Value};");
                        hasAttribute = true;
                    }

                    if (hasAttribute)
                    {
                        await WriteLineAsync("");
                    }
                }

                return;

                async Task WriteLineAsync(string text)
                {
                    await httpContext.Response.WriteAsync(text);
                    await httpContext.Response.WriteAsync(Environment.NewLine);
                }

                Task WriteAsync(string text)
                {
                    return httpContext.Response.WriteAsync(text);
                }
            }

            await _next(httpContext);

        }
    }

    public static class DebugInfoPageMiddlewareExtensions
    {
        public static IApplicationBuilder RunDebugInfoPage(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<DebugInfoPageMiddleware>(builder.ServerFeatures.Get<IServerAddressesFeature>() ?? new EmptyServerAddressesFeature());
        }

        private class EmptyServerAddressesFeature: IServerAddressesFeature
        {
            public ICollection<string> Addresses { get; } = new List<string>();
            public bool PreferHostingUrls { get; set; }
        }
    }
}
