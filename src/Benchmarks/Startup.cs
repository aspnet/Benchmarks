// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Server.Kestrel;
using Microsoft.Extensions.DependencyInjection;

namespace Benchmarks
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
            services.AddSingleton(typeof(IHttpContextAccessor), typeof(InertHttpContextAccessor));
        }

        public void Configure(IApplicationBuilder app)
        {
            var kestrel = app.ServerFeatures[typeof(IKestrelServerInformation)] as IKestrelServerInformation;

            if (kestrel != null)
            {
                // BUG: Multi-loop Kestrel doesn't work on Windows right now, see https://github.com/aspnet/KestrelHttpServer/issues/232
                // kestrel.ThreadCount = 2;
            }

            app.UsePlainText();
            app.UseJson();
            app.UseMvc();
        }
    }
}
