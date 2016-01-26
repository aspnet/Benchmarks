// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Data.Common;
using System.Data.SqlClient;
using Benchmarks.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Benchmarks
{
    public class Startup
    {
        public Startup(IHostingEnvironment env, Scenarios scenarios)
        {
            // Set up configuration sources.
            var builder = new ConfigurationBuilder()
                .Include(env.Configuration)
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();

            Configuration.Bind(StartupOptions);

            Scenarios = scenarios ?? new Scenarios();
        }

        public IConfigurationRoot Configuration { get; set; }

        public Options StartupOptions { get; } = new Options();
        
        public Scenarios Scenarios { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            // No scenarios covered by the benchmarks require the HttpContextAccessor so we're replacing it with a
            // no-op version to avoid the cost.
            services.AddSingleton(typeof(IHttpContextAccessor), typeof(InertHttpContextAccessor));

            if (Scenarios.Any("Db"))
            {
                services.AddSingleton<ApplicationDbSeeder>();
                // TODO: Add support for plugging in different DbProviderFactory implementations via configuration
                services.AddSingleton<DbProviderFactory>(SqlClientFactory.Instance);
            }

            if (Scenarios.Any("Ef"))
            {
                services.AddEntityFramework()
                    .AddSqlServer()
                    .AddDbContext<ApplicationDbContext>(options =>
                        options.UseSqlServer(StartupOptions.ConnectionString));
            }

            if (Scenarios.Any("Fortunes"))
            {
                services.AddWebEncoders();
            }

            if (Scenarios.Any("Mvc"))
            {
                var mvcBuilder = services.AddMvcCore();
                
                if (Scenarios.MvcViews)
                {
                    mvcBuilder
                        .AddViews()
                        .AddRazorViewEngine();
                }
            }
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseErrorHandler();

            if (Scenarios.Plaintext)
            {
                app.UsePlainText();
            }

            if (Scenarios.Json)
            {
                app.UseJson();
            }

            // Single query endpoints
            if (Scenarios.DbSingleQueryRaw)
            {
                app.UseSingleQueryRaw(StartupOptions.ConnectionString);
            }

            if (Scenarios.DbSingleQueryDapper)
            {
                app.UseSingleQueryDapper(StartupOptions.ConnectionString);
            }

            if (Scenarios.DbSingleQueryEf)
            {
                app.UseSingleQueryEf();
            }

            // Multiple query endpoints
            if (Scenarios.DbMultiQueryRaw)
            {
                app.UseMultipleQueriesRaw(StartupOptions.ConnectionString);
            }

            if (Scenarios.DbMultiQueryDapper)
            {
                app.UseMultipleQueriesDapper(StartupOptions.ConnectionString);
            }

            if (Scenarios.DbMultiQueryEf)
            {
                app.UseMultipleQueriesEf();
            }

            if (Scenarios.Any("Db"))
            {
                var dbContext = (ApplicationDbContext)app.ApplicationServices.GetService(typeof(ApplicationDbContext));
                var seeder = (ApplicationDbSeeder)app.ApplicationServices.GetService(typeof(ApplicationDbSeeder));
                if (!seeder.Seed(dbContext))
                {
                    Environment.Exit(1);
                }
            }

            if (Scenarios.Any("Mvc"))
            {
                app.UseMvc();
            }

            if (Scenarios.StaticFiles)
            {
                app.UseStaticFiles();
            }

            app.UseDebugInfoPage();

            app.Run(context => context.Response.WriteAsync("Try /plaintext instead, or /debug for more information"));
        }

        public class Options
        {
            public string ConnectionString { get; set; }
        }

        public class InertHttpContextAccessor : IHttpContextAccessor
        {
            public HttpContext HttpContext
            {
                get { return null; }
                set { return; }
            }
        }
    }
}
