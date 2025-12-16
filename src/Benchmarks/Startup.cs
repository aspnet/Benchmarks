// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using Microsoft.Data.SqlClient;
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
using Microsoft.Extensions.Hosting;
using Npgsql;
using System.IO;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore.Storage;

namespace Benchmarks
{
    public class Startup
    {
        public Startup(IWebHostEnvironment hostingEnv)
        {
            // Set up configuration sources.
            var builder = new ConfigurationBuilder()
                .SetBasePath(hostingEnv.ContentRootPath)
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{hostingEnv.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(Program.Args);

            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; set; }

        public Scenarios Scenarios { get; private set; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<AppSettings>(Configuration);

            // Retrieve Scenarios that was registered in Program.Main
            // We need it throughout ConfigureServices, so we must call BuildServiceProvider here.
            // Warning suppression justification:
            // Duplicate Scenarios will not cause issues as it is a singleton and we re-register the same instance below.
            // Moving to a different pattern would require significant refactoring.
#pragma warning disable ASP0000 // Do not call 'BuildServiceProvider' in 'ConfigureServices'
            using (var serviceProvider = services.BuildServiceProvider())
            {
                Scenarios = serviceProvider.GetRequiredService<Scenarios>();
            }
#pragma warning restore ASP0000

            // Common DB services
            services.AddSingleton<IRandom, DefaultRandom>();

            var appSettings = Configuration.Get<AppSettings>();
            BatchUpdateString.DatabaseServer = appSettings.Database;

            Console.WriteLine($"Database: {appSettings.Database}");
            Console.WriteLine($"ConnectionString: {appSettings.ConnectionString}");

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
                                , o => o.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d))
                                )
                            .EnableThreadSafetyChecks(false)
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
                    throw new NotSupportedException("EF/MySQL is unsupported");

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
                        command.CommandText = "PRAGMA journal_mode = 'wal'";
                        command.ExecuteNonQuery();
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
            }

            if (Scenarios.Any("MemoryCache"))
            {
                services.AddMemoryCache();
            }

            if (Scenarios.Any("ResponseCaching"))
            {
                services.AddResponseCaching();
            }
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
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
