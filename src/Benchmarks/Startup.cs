// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using Benchmarks.Data;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Server.Kestrel;
using Microsoft.Data.Entity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Benchmarks
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            // Set up configuration sources.
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();

            Configuration.Bind(StartupOptions);
        }

        public IConfigurationRoot Configuration { get; set; }

        public Options StartupOptions { get; } = new Options();

        public void ConfigureServices(IServiceCollection services)
        {
            // No scenarios covered by the benchmarks require the HttpContextAccessor so we're replacing it with a
            // no-op version to avoid the cost.
            services.AddSingleton(typeof(IHttpContextAccessor), typeof(InertHttpContextAccessor));

            if (StartupOptions.EnableDbTests)
            {
                services.AddSingleton<ApplicationDbSeeder>();
                services.AddEntityFramework()
                    .AddSqlServer()
                    .AddDbContext<ApplicationDbContext>(options =>
                        options.UseSqlServer(StartupOptions.ConnectionString));
            }

            services.AddMvc();
        }

        public void Configure(IApplicationBuilder app)
        {
            var kestrel = app.ServerFeatures[typeof(IKestrelServerInformation)] as IKestrelServerInformation;

            if (kestrel != null)
            {
                // Using an I/O thread for every 2 logical CPUs appears to be a good ratio
                kestrel.ThreadCount = Environment.ProcessorCount >> 1;
                kestrel.NoDelay = true;
            }

            app.UseErrorHandler();
            app.UsePlainText();
            app.UseJson();

            if (StartupOptions.EnableDbTests)
            {
                app.UseSingleQueryRaw(StartupOptions.ConnectionString);
                app.UseSingleQueryEf();
                app.UseSingleQueryDapper(StartupOptions.ConnectionString);

                var dbContext = (ApplicationDbContext)app.ApplicationServices.GetService(typeof(ApplicationDbContext));
                var seeder = (ApplicationDbSeeder)app.ApplicationServices.GetService(typeof(ApplicationDbSeeder));
                if (!seeder.Seed(dbContext))
                {
                    Environment.Exit(1);
                }
                Console.WriteLine("Database tests enabled");
            }

            app.UseMvc();

            app.Run(context => context.Response.WriteAsync("Try /plaintext instead"));
        }
        
        public class InertHttpContextAccessor : IHttpContextAccessor
        {
            public HttpContext HttpContext
            {
                get { return null; }
                set { return; }
            }
        }

        public class Options
        {
            public bool EnableDbTests { get; set; }

            public string ConnectionString { get; set; }
        }
    }
}
