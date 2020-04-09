using System;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace PlatformBenchmarks
{
    // copy of https://github.com/dotnet/aspnetcore/blob/master/src/Servers/Kestrel/Core/src/Internal/KestrelServerOptionsSetup.cs
    internal sealed class KestrelServerOptionsSetup : IConfigureOptions<KestrelServerOptions>
    {
        private readonly IServiceProvider _services;

        public KestrelServerOptionsSetup(IServiceProvider services) => _services = services;

        public void Configure(KestrelServerOptions options) => options.ApplicationServices = _services;
    }
}