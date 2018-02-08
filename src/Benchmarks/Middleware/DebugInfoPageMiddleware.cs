// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            httpContext.Response.ContentType = "text/html";

            // If the diagnostics were explicitly requested we return 200 OK
            httpContext.Response.StatusCode = httpContext.Request.Path == "/diagnostics"
                ? StatusCodes.Status200OK
                : StatusCodes.Status404NotFound;

            await WriteLineAsync("<!DOCTYPE html><html><head><style>body{font-family:\"Segoe UI\",Arial,Helvetica,Sans-serif};h1,h2,h3{font-family:\"Segoe UI Light\"}</style></head><body>");
            await WriteLineAsync("<h1>ASP.NET Core Benchmarks</h1>");
            await WriteLineAsync("<h2>Configuration Information</h2>");
            await WriteLineAsync("<ul>");
            await WriteLineAsync($"<li>Environment: {_hostingEnv.EnvironmentName}</li>");
            await WriteLineAsync($"<li>Framework: {_targetFrameworkName}</li>");
            await WriteLineAsync($"<li>Server GC enabled: {GCSettings.IsServerGC}</li>");
            await WriteLineAsync($"<li>Configuration: {_configurationName}</li>");
            await WriteLineAsync($"<li>Server: {Program.Server}</li>");
            await WriteLineAsync($"<li>Server URLs: {string.Join(", ", _serverAddresses.Addresses)}</li>");
            await WriteLineAsync($"<li>Supports Send File: {httpContext.Features.Get<IHttpSendFileFeature>() != null}</li>");

            await WriteLineAsync($"<li>Environment variables:<ul>");
            foreach (DictionaryEntry ev in Environment.GetEnvironmentVariables())
            {
                await WriteLineAsync($"<li>{ev.Key} = {ev.Value}</li>");
            }
            await WriteLineAsync($"</ul></li>");

            await WriteLineAsync($"<li>Server features:<ul>");
            foreach (var feature in httpContext.Features)
            {
                await WriteLineAsync($"<li>{feature.Key.Name}</li>");
            }
            await WriteLineAsync($"</ul></li>");

            await WriteLineAsync($"<li>Enabled scenarios:<ul>");
            var enabledScenarios = _scenarios.GetEnabled();
            var maxNameLength = enabledScenarios.Max(s => s.Name.Length);
            foreach (var scenario in enabledScenarios)
            {
                await WriteLineAsync($"<li>{scenario.Name}<ul>");
                foreach (var path in scenario.Paths)
                {
                    await WriteLineAsync($"<li><a href=\"{path}\">{path}</a></li>");
                }
                await WriteLineAsync($"</ul></li>");
            }
            await WriteLineAsync($"</ul></li>");


            await WriteLineAsync($"<li>Loaded modules:<ul>");

            foreach (var m in Process.GetCurrentProcess().Modules.OfType<ProcessModule>())
            {
                Assembly assembly = null;
                try
                {
                    assembly = Assembly.Load(new AssemblyName(Path.GetFileNameWithoutExtension(m.ModuleName)));
                }
                catch { }

                await WriteLineAsync($"<li>{m.FileName} {m.ModuleName} {assembly?.GetName().Version.ToString()} {assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}</li>");
            }

            await WriteLineAsync("</ul></li>");

            await WriteLineAsync("</ul>");

            await WriteLineAsync("</body></html>");

            async Task WriteLineAsync(string text)
            {
                await httpContext.Response.WriteAsync(text);
                await httpContext.Response.WriteAsync(Environment.NewLine);
            }
        }
    }

    public static class DebugInfoPageMiddlewareExtensions
    {
        public static IApplicationBuilder RunDebugInfoPage(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<DebugInfoPageMiddleware>(builder.ServerFeatures.Get<IServerAddressesFeature>());
        }
    }
}
