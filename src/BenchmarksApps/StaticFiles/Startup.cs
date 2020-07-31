// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace StaticFiles
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            string filename = "";
            int size = 0;

            if (_configuration["filename"] != null)
            {
                filename = Path.GetFileName(_configuration["filename"]);
            }

            if (_configuration["size"] != null)
            {
                Int32.TryParse(_configuration["size"], out size);
            }

            if (String.IsNullOrEmpty(filename))
            {
                filename = "file.txt";
            }

            if (size == 0)
            {
                size = 1024;
            }

            var localFileName = Path.Combine(env.WebRootPath, filename);

            Console.WriteLine($"Creating {localFileName} with {size} bytes of data");

            File.WriteAllText(localFileName, new String('A', size));

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseStaticFiles();

            Console.WriteLine($"AspNetCore: {typeof(IWebHostBuilder).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
            Console.WriteLine($"NETCoreApp: {typeof(object).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        }
    }
}
