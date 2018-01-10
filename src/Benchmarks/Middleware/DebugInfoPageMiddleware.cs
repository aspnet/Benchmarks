// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
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

            var NL = Environment.NewLine;

            await httpContext.Response.WriteAsync($"ASP.NET Core Benchmarks {NL}");
            await httpContext.Response.WriteAsync($"----------------------- {NL}");

            await httpContext.Response.WriteAsync($"Configuration Information {NL}");
            await httpContext.Response.WriteAsync($"Environment: {_hostingEnv.EnvironmentName} {NL}");
            await httpContext.Response.WriteAsync($"Framework: {_targetFrameworkName} {NL}");
            await httpContext.Response.WriteAsync($"Server GC enabled: {GCSettings.IsServerGC} {NL}");
            await httpContext.Response.WriteAsync($"Configuration: {_configurationName} {NL}");
            await httpContext.Response.WriteAsync($"Server: {Program.Server} {NL}");
            await httpContext.Response.WriteAsync($"Server URLs: {string.Join(", ", _serverAddresses.Addresses)} {NL}");
            await httpContext.Response.WriteAsync($"Supports Send File: {httpContext.Features.Get<IHttpSendFileFeature>() != null} {NL}");

            await httpContext.Response.WriteAsync($"Server features: {NL}");
            foreach (var feature in httpContext.Features)
            {
                await httpContext.Response.WriteAsync($"{feature.Key.Name} {NL}");
            }
            await httpContext.Response.WriteAsync(NL);

            await httpContext.Response.WriteAsync($"Enabled scenarios: {NL}");
            var enabledScenarios = _scenarios.GetEnabled();
            var maxNameLength = enabledScenarios.Max(s => s.Name.Length);
            foreach (var scenario in enabledScenarios)
            {
                await httpContext.Response.WriteAsync($"{scenario.Name} {NL}");
                foreach (var path in scenario.Paths)
                {
                    await httpContext.Response.WriteAsync($"<a href=\"{path}\">{path}</a> {NL}");
                }
                await httpContext.Response.WriteAsync(NL);
            }
            await httpContext.Response.WriteAsync(NL);


            await httpContext.Response.WriteAsync($"Loaded modules: {NL}");

            foreach (var m in Process.GetCurrentProcess().Modules.OfType<ProcessModule>())
            {
                Assembly assembly = null;
                try
                {
                    assembly = Assembly.Load(new AssemblyName(Path.GetFileNameWithoutExtension(m.ModuleName)));
                }
                catch { }

                await httpContext.Response.WriteAsync($"{m.FileName} {m.ModuleName} {assembly?.GetName().Version.ToString()} {assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} {NL}");
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
