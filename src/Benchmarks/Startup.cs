// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using System.Data.SqlClient;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Benchmarks.Configuration;
using Benchmarks.Data;
using Benchmarks.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Npgsql;
using System.IO;
using Microsoft.AspNetCore.WebUtilities;

namespace Benchmarks
{
    public class Startup
    {
        public Startup(IHostingEnvironment hostingEnv, Scenarios scenarios)
        {
            // Set up configuration sources.
            var builder = new ConfigurationBuilder()
                .SetBasePath(hostingEnv.ContentRootPath)
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{hostingEnv.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(Program.Args);

            Configuration = builder.Build();

            Scenarios = scenarios;
        }

        public IConfigurationRoot Configuration { get; set; }

        public Scenarios Scenarios { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<AppSettings>(Configuration);

            // We re-register the Scenarios as an instance singleton here to avoid it being created again due to the
            // registration done in Program.Main
            services.AddSingleton(Scenarios);

            // Common DB services
            services.AddSingleton<IRandom, DefaultRandom>();

            var appSettings = Configuration.Get<AppSettings>();

            Console.WriteLine($"Database: {appSettings.Database}");
            Console.WriteLine($"ConnectionString: {appSettings.ConnectionString}");

            switch (appSettings.Database)
            {
                case DatabaseServer.PostgreSql:
                    services.AddEntityFrameworkNpgsql();
                    var settings = new NpgsqlConnectionStringBuilder(appSettings.ConnectionString);
                    if (!settings.NoResetOnClose)
                        throw new ArgumentException("No Reset On Close=true must be specified for Npgsql");
                    if (settings.Enlist)
                        throw new ArgumentException("Enlist=false must be specified for Npgsql");

                    services.AddDbContextPool<ApplicationDbContext>(options => options.UseNpgsql(appSettings.ConnectionString));

                    if (Scenarios.Any("Raw") || Scenarios.Any("Dapper"))
                    {
                        services.AddSingleton<DbProviderFactory>(NpgsqlFactory.Instance);
                    }
                    break;

                case DatabaseServer.SqlServer:
                    services.AddEntityFrameworkSqlServer();
                    services.AddDbContextPool<ApplicationDbContext>(options => options.UseSqlServer(appSettings.ConnectionString));

                    if (Scenarios.Any("Raw") || Scenarios.Any("Dapper"))
                    {
                        services.AddSingleton<DbProviderFactory>(SqlClientFactory.Instance);
                    }
                    break;

                case DatabaseServer.MySql:

#if NETCOREAPP3_0
                    throw new NotSupportedException("EF/MySQL is unsupported on netcoreapp3.0 until a provider is available");
#else
                    services.AddEntityFrameworkMySql();
                    services.AddDbContextPool<ApplicationDbContext>(options => options.UseMySql(appSettings.ConnectionString));

                    if (Scenarios.Any("Raw") || Scenarios.Any("Dapper"))
                    {
                        services.AddSingleton<DbProviderFactory>(MySql.Data.MySqlClient.MySqlClientFactory.Instance);
                    }

                    break;
#endif

                case DatabaseServer.MongoDb:
                    var mongoClient = new MongoClient(appSettings.ConnectionString);
                    var mongoDatabase = mongoClient.GetDatabase("hello_world");
                    services.AddSingleton(mongoClient);
                    services.AddSingleton(mongoDatabase);
                    services.AddSingleton(sp => mongoDatabase.GetCollection<Fortune>("fortune"));
                    services.AddSingleton(sp => mongoDatabase.GetCollection<World>("world"));
                    break;
            }

            if (Scenarios.Any("Ef"))
            {
                services.AddScoped<EfDb>();
            }

            if (Scenarios.Any("Raw"))
            {
                services.AddScoped<RawDb>();
            }

            if (Scenarios.Any("Dapper"))
            {
                services.AddScoped<DapperDb>();
            }

            if (Scenarios.Any("Mongo"))
            {
                services.AddScoped<MongoDb>();
            }

            if (Scenarios.Any("Update"))
            {
                BatchUpdateString.Initalize();
            }

            if (Scenarios.Any("Fortunes"))
            {
                var settings = new TextEncoderSettings(UnicodeRanges.BasicLatin, UnicodeRanges.Katakana, UnicodeRanges.Hiragana);
                settings.AllowCharacter('\u2014');  // allow EM DASH through
                services.AddWebEncoders((options) =>
                {
                    options.TextEncoderSettings = settings;
                });
            }

#if NETCOREAPP2_1 || NETCOREAPP2_2
            if (Scenarios.Any("Mvc"))
            {
                var mvcBuilder = services
                    .AddMvcCore()
                    .SetCompatibilityVersion(CompatibilityVersion.Latest);

                if (Scenarios.MvcJson || Scenarios.Any("MvcDbSingle") || Scenarios.Any("MvcDbMulti"))
                {
                    mvcBuilder.AddJsonFormatters();
                }
                if (Scenarios.MvcViews || Scenarios.Any("MvcDbFortunes"))
                {
                    mvcBuilder
                        .AddViews()
                        .AddRazorViewEngine();
                }
            }
#elif NETCOREAPP3_0
            if (Scenarios.Any("Endpoint"))
            {
                services.AddRouting();
            }

            if (Scenarios.Any("Mvc"))
            {
                IMvcBuilder builder;

                if (Scenarios.MvcViews || Scenarios.Any("MvcDbFortunes"))
                {
                    builder = services.AddControllersWithViews();
                }
                else
                {
                    builder = services.AddControllers();
                }

                if (Scenarios.Any("MvcJsonNet"))
                {
                    builder.AddNewtonsoftJson();
                }
            }
#else
#error "Unsupported TFM"
#endif

            if (Scenarios.Any("MemoryCache"))
            {
                services.AddMemoryCache();
            }

            if (Scenarios.Any("ResponseCaching"))
            {
                services.AddResponseCaching();
            }
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            if (Scenarios.StaticFiles)
            {
                app.UseStaticFiles();
            }

            if (Scenarios.StaticFiles || Scenarios.Plaintext)
            {
                app.UsePlainText();
            }

            if (Scenarios.Json)
            {
                app.UseJson();
            }

            if (Scenarios.CopyToAsync)
            {
                app.UseCopyToAsync();
            }

            // Single query endpoints
            if (Scenarios.DbSingleQueryRaw)
            {
                app.UseSingleQueryRaw();
            }

            if (Scenarios.DbSingleQueryDapper)
            {
                app.UseSingleQueryDapper();
            }

            if (Scenarios.DbSingleQueryMongoDb)
            {
                app.UseSingleQueryMongoDb();
            }

            if (Scenarios.DbSingleQueryEf)
            {
                app.UseSingleQueryEf();
            }

            // Multiple query endpoints
            if (Scenarios.DbMultiQueryRaw)
            {
                app.UseMultipleQueriesRaw();
            }

            if (Scenarios.DbMultiQueryDapper)
            {
                app.UseMultipleQueriesDapper();
            }

            if (Scenarios.DbMultiQueryMongoDb)
            {
                app.UseMultipleQueriesMongoDb();
            }

            if (Scenarios.DbMultiQueryEf)
            {
                app.UseMultipleQueriesEf();
            }

            // Multiple update endpoints
            if (Scenarios.DbMultiUpdateRaw)
            {
                app.UseMultipleUpdatesRaw();
            }

            if (Scenarios.DbMultiUpdateDapper)
            {
                app.UseMultipleUpdatesDapper();
            }

            if (Scenarios.DbMultiUpdateEf)
            {
                app.UseMultipleUpdatesEf();
            }

            // Fortunes endpoints
            if (Scenarios.DbFortunesRaw)
            {
                app.UseFortunesRaw();
            }

            if (Scenarios.DbFortunesRawSync)
            {
                app.UseFortunesRawSync();
            }

            if (Scenarios.DbFortunesDapper)
            {
                app.UseFortunesDapper();
            }

            if (Scenarios.DbFortunesMongoDb)
            {
                app.UseFortunesMongoDb();
            }

            if (Scenarios.DbFortunesEf)
            {
                app.UseFortunesEf();
            }

#if NETCOREAPP2_1 || NETCOREAPP2_2
            if (Scenarios.Any("Mvc"))
            {
                app.UseMvc();
            }
#elif NETCOREAPP3_0
            if (Scenarios.Any("EndpointPlaintext"))
            {
                app.UseRouting();

                app.UseEndpoints(endpoints =>
                {
                    var _helloWorldPayload = Encoding.UTF8.GetBytes("Hello, World!");

                    endpoints.Map(
                        requestDelegate: context =>
                        {
                            var payloadLength = _helloWorldPayload.Length;
                            var response = context.Response;
                            response.StatusCode = 200;
                            response.ContentType = "text/plain";
                            response.ContentLength = payloadLength;
                            return response.Body.WriteAsync(_helloWorldPayload, 0, payloadLength);
                        },
                        pattern: "/ep-plaintext");
                });
            }

            if (Scenarios.Any("Mvc"))
            {
                app.UseRouting();
            
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            }
#endif

            if (Scenarios.MemoryCachePlaintext)
            {
                app.UseMemoryCachePlaintext();
            }

            if (Scenarios.MemoryCachePlaintextSetRemove)
            {
                app.UseMemoryCachePlaintextSetRemove();
            }

            if (Scenarios.ResponseCachingPlaintextCached)
            {
                app.UseResponseCachingPlaintextCached();
            }

            if (Scenarios.ResponseCachingPlaintextResponseNoCache)
            {
                app.UseResponseCachingPlaintextResponseNoCache();
            }

            if (Scenarios.ResponseCachingPlaintextRequestNoCache)
            {
                app.UseResponseCachingPlaintextRequestNoCache();
            }

            if (Scenarios.ResponseCachingPlaintextVaryByCached)
            {
                app.UseResponseCachingPlaintextVaryByCached();
            }

            app.UseAutoShutdown();

            app.RunDebugInfoPage();
        }
    }
}
