// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
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
        public Startup(IHostingEnvironment env)
        {
            // Set up configuration sources.
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(Environment.GetCommandLineArgs());

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

            if (StartupOptions.Scenarios.Any("Db"))
            {
                services.AddSingleton<ApplicationDbSeeder>();
                // TODO: Add support for plugging in different DbProviderFactory implementations via configuration
                services.AddSingleton<DbProviderFactory>(SqlClientFactory.Instance);
            }

            if (StartupOptions.Scenarios.Any("Ef"))
            {
                services.AddEntityFramework()
                    .AddSqlServer()
                    .AddDbContext<ApplicationDbContext>(options =>
                        options.UseSqlServer(StartupOptions.ConnectionString));
            }

            if (StartupOptions.Scenarios.Any("Fortunes"))
            {
                services.AddWebEncoders();
            }

            if (StartupOptions.Scenarios.MvcApis)
            {
                services.AddMvcCore();
            }
            else if (StartupOptions.Scenarios.MvcViews)
            {
                services.AddMvcCore()
                    .AddViews()
                    .AddRazorViewEngine();
            }
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseErrorHandler();

            if (StartupOptions.Scenarios.Plaintext)
            {
                app.UsePlainText();
            }

            if (StartupOptions.Scenarios.Json)
            {
                app.UseJson();
            }

            // Single query endpoints
            if (StartupOptions.Scenarios.DbSingleQueryRaw)
            {
                app.UseSingleQueryRaw(StartupOptions.ConnectionString);
            }

            if (StartupOptions.Scenarios.DbSingleQueryDapper)
            {
                app.UseSingleQueryDapper(StartupOptions.ConnectionString);
            }

            if (StartupOptions.Scenarios.DbSingleQueryEf)
            {
                app.UseSingleQueryEf();
            }

            // Multiple query endpoints
            if (StartupOptions.Scenarios.DbMultiQueryRaw)
            {
                app.UseMultipleQueriesRaw(StartupOptions.ConnectionString);
            }

            if (StartupOptions.Scenarios.DbMultiQueryDapper)
            {
                app.UseMultipleQueriesDapper(StartupOptions.ConnectionString);
            }

            if (StartupOptions.Scenarios.DbMultiQueryEf)
            {
                app.UseMultipleQueriesEf();
            }

            if (StartupOptions.Scenarios.Any("Db"))
            {
                var dbContext = (ApplicationDbContext)app.ApplicationServices.GetService(typeof(ApplicationDbContext));
                var seeder = (ApplicationDbSeeder)app.ApplicationServices.GetService(typeof(ApplicationDbSeeder));
                if (!seeder.Seed(dbContext))
                {
                    Environment.Exit(1);
                }
            }

            if (StartupOptions.Scenarios.Any("Mvc"))
            {
                app.UseMvc();
            }

            if (StartupOptions.Scenarios.StaticFiles)
            {
                app.UseStaticFiles();
            }

            app.UseDebugInfoPage();

            app.Run(context => context.Response.WriteAsync("Try /plaintext instead"));
        }

        public class Options
        {
            public string ConnectionString { get; set; }

            public Scenarios Scenarios { get; } = new Scenarios();
        }

        public class Scenarios
        {
            public bool All { get; set; }

            public bool Plaintext { get; set; }

            public bool Json { get; set; }

            public bool StaticFiles { get; set; }

            public bool MvcApis { get; set; }

            public bool MvcViews { get; set; }

            public bool DbSingleQueryRaw { get; set; }

            public bool DbSingleQueryEf { get; set; }

            public bool DbSingleQueryDapper { get; set; }

            public bool DbMultiQueryRaw { get; set; }

            public bool DbMultiQueryEf { get; set; }

            public bool DbMultiQueryDapper { get; set; }

            public bool DbFortunesRaw { get; set; }

            public bool DbFortunesEf { get; set; }

            public bool DbFortunesDapper { get; set; }

            public bool Any(string partialName) =>
                typeof(Scenarios).GetTypeInfo().DeclaredProperties
                    .Where(p => p.Name.IndexOf(partialName, StringComparison.Ordinal) >= 0 && (bool)p.GetValue(this))
                    .Any();

            public IEnumerable<string> Names =>
                typeof(Scenarios).GetTypeInfo().DeclaredProperties
                    .Select(p => p.Name);
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
