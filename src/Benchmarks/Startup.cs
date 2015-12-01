// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Data.Common;
using System.Data.SqlClient;
using Benchmarks.Data;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Http;
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

            if (env.Configuration != null)
            {
                // This allows passing of config values via the cmd line, e.g.: dnx web --app.EnableDbTests=true
                builder.AddConfiguration("app.", env.Configuration);
            }

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

                // TODO: Add support for plugging in different DbProviderFactory implementations via configuration
                services.AddSingleton<DbProviderFactory>(SqlClientFactory.Instance);

                services.AddEntityFramework()
                    .AddSqlServer()
                    .AddDbContext<ApplicationDbContext>(options =>
                        options.UseSqlServer(StartupOptions.ConnectionString));
                services.AddWebEncoders();
            }

            services.AddMvc();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseErrorHandler();
            app.UsePlainText();
            app.UseJson();

            if (StartupOptions.EnableDbTests)
            {
                app.UseSingleQueryRaw(StartupOptions.ConnectionString);
                app.UseSingleQueryDapper(StartupOptions.ConnectionString);
                app.UseSingleQueryEf();

                app.UseMultipleQueriesRaw(StartupOptions.ConnectionString);
                app.UseMultipleQueriesDapper(StartupOptions.ConnectionString);
                app.UseMultipleQueriesEf();

                app.UseFortunesRaw(StartupOptions.ConnectionString);
                app.UseFortunesDapper(StartupOptions.ConnectionString);
                app.UseFortunesEf();

                var dbContext = (ApplicationDbContext)app.ApplicationServices.GetService(typeof(ApplicationDbContext));
                var seeder = (ApplicationDbSeeder)app.ApplicationServices.GetService(typeof(ApplicationDbSeeder));
                if (!seeder.Seed(dbContext))
                {
                    Environment.Exit(1);
                }
                Console.WriteLine("Database tests enabled");
            }

            app.UseMvc();

            if (StartupOptions.EnableStaticFileTests)
            {
                app.UseStaticFiles();
                Console.WriteLine("Static file tests enabled");
            }

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

            public bool EnableStaticFileTests { get; set; }

            public string ConnectionString { get; set; }
        }
    }
}
