// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Server.Kestrel;
using Microsoft.Framework.DependencyInjection;

namespace Benchmarks
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
        }

        public void Configure(IApplicationBuilder app)
        {
            var kestrel = app.ServerFeatures[typeof(IKestrelServerInformation)] as IKestrelServerInformation;

            if (kestrel != null)
            {
                kestrel.ThreadCount = Environment.ProcessorCount / 2;
            }

            app.UsePlainText();
            app.UseJson();
            app.UseMvc();
        }
    }
}
