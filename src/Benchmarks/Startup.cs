// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
#if NETCOREAPP3_0 || NETCOREAPP3_1 || NETCOREAPP5_0 || NET5_0 || NET6_0
using Microsoft.Data.SqlClient;
#else
using System.Data.SqlClient;
#endif
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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.IO;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore.Storage;

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
            BatchUpdateString.DatabaseServer = appSettings.Database;

            Console.WriteLine($"Database: {appSettings.Database}");
            Console.WriteLine($"ConnectionString: {appSettings.ConnectionString}");
            Console.WriteLine($"WAL: {appSettings.WAL}");

            switch (appSettings.Database)
            {
                case DatabaseServer.PostgreSql:
                    services.AddEntityFrameworkNpgsql();
                    var settings = new NpgsqlConnectionStringBuilder(appSettings.ConnectionString);
                    if (!settings.NoResetOnClose && !settings.Multiplexing)
                        throw new ArgumentException("No Reset On Close=true Or Multiplexing=true implies must be specified for Npgsql");
                    if (settings.Enlist)
                        throw new ArgumentException("Enlist=false must be specified for Npgsql");

                    services.AddDbContextPool<ApplicationDbContext>(
                        options => options
                            .UseNpgsql(appSettings.ConnectionString
#if NET5_0_OR_GREATER
                                , o => o.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d))
#endif
                                )
#if NET6_0_OR_GREATER
                            .DisableConcurrencyDetection()
#endif
                        , 1024);

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

#if NETCOREAPP3_0 || NETCOREAPP3_1 || NETCOREAPP5_0 || NET5_0 || NET6_0
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

                case DatabaseServer.Sqlite:
                    using (var connection = new SqliteConnection(appSettings.ConnectionString))
                    {
                        SqliteCommand command;

                        if (!File.Exists(connection.DataSource))
                        {
                            connection.Open();

                            command = connection.CreateCommand();
                            command.CommandText =
                            @"
                                CREATE TABLE world (
                                    id INTEGER NOT NULL PRIMARY KEY,
                                    randomNumber INTEGER NOT NULL
                                );

                                CREATE TABLE fortune (
                                    id INTEGER NOT NULL PRIMARY KEY,
                                    message TEXT NOT NULL
                                );

                                INSERT INTO fortune (message)
                                VALUES
                                    ('fortune: No such file or directory'),
                                    ('A computer scientist is someone who fixes things that aren''t broken.'),
                                    ('After enough decimal places, nobody gives a damn.'),
                                    ('A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1'),
                                    ('A computer program does what you tell it to do, not what you want it to do.'),
                                    ('Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen'),
                                    ('Any program that runs right is obsolete.'),
                                    ('A list is only as strong as its weakest link. — Donald Knuth'),
                                    ('Feature: A bug with seniority.'),
                                    ('Computers make very fast, very accurate mistakes.'),
                                    ('<script>alert(""This should not be displayed in a browser alert box."");</script>'),
                                    ('フレームワークのベンチマーク');
                            ";
                            command.ExecuteNonQuery();

                            using (var transaction = connection.BeginTransaction())
                            {
                                command.CommandText = "INSERT INTO world (randomNumber) VALUES (@Value)";
                                command.Transaction = transaction;
                                var parameter = command.CreateParameter();
                                parameter.ParameterName = "@Value";
                                command.Parameters.Add(parameter);

                                var random = new Random();
                                for (var x = 0; x < 10000; x++)
                                {
                                    parameter.Value = random.Next(1, 10001);
                                    command.ExecuteNonQuery();
                                }

                                transaction.Commit();
                            }
                        }

                        connection.Open();

                        command = connection.CreateCommand();
                        command.CommandText = "PRAGMA journal_mode";
                        var currentMode = (string)command.ExecuteScalar();

                        if (appSettings.WAL && currentMode != "wal")
                        {
                            command.CommandText = "PRAGMA journal_mode = 'wal'";
                            command.ExecuteNonQuery();
                        }
                        else if (!appSettings.WAL && currentMode == "wal")
                        {
                            command.CommandText = "PRAGMA journal_mode = 'delete'";
                            command.ExecuteNonQuery();
                        }
                    }

                    services.AddEntityFrameworkSqlite();
                    services.AddDbContextPool<ApplicationDbContext>(options => options.UseSqlite(appSettings.ConnectionString));

                    if (Scenarios.Any("Raw") || Scenarios.Any("Dapper"))
                    {
                        services.AddSingleton<DbProviderFactory>(SqliteFactory.Instance);
                    }
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
#elif NETCOREAPP3_0 || NETCOREAPP3_1 || NETCOREAPP5_0 || NET5_0 || NET6_0
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

            if (Scenarios.DbFortunesEf)
            {
                app.UseFortunesEf();
            }

#if NETCOREAPP2_1 || NETCOREAPP2_2
            if (Scenarios.Any("Mvc"))
            {
                app.UseMvc();
            }
#elif NETCOREAPP3_0 || NETCOREAPP3_1 || NETCOREAPP5_0 || NET5_0 || NET6_0
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
        }
    }
}
