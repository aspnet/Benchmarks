// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading;
using Benchmarks.Configuration;
using Benchmarks.Data;
using Benchmarks.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Benchmarks
{
    public class Program
    {
        private static string Server;
        private static string Protocol;

        public static void Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("ASP.NET Core Benchmarks");
            Console.WriteLine("-----------------------");

            Console.WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");

            // Console.WriteLine($"AspNetCore location: {typeof(IWebHostBuilder).GetTypeInfo().Assembly.Location}");
            // Console.WriteLine($".NET Runtime location: {typeof(object).GetTypeInfo().Assembly.Location}");

            Console.WriteLine($"AspNetCore version: {typeof(IWebHostBuilder).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
            Console.WriteLine($".NET Runtime version: {typeof(object).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
            Console.WriteLine($"EFCore version: {typeof(DbContext).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");

            Console.WriteLine($"Environment.ProcessorCount: {Environment.ProcessorCount}");

            var hostingConfig = new ConfigurationBuilder()
                .AddJsonFile("hosting.json", optional: true)
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .AddCommandLine(args)
                .Build();

            Server = hostingConfig["server"] ?? "Kestrel";
            Protocol = hostingConfig["protocol"] ?? "";

            var builder = WebApplication.CreateBuilder(args);

            builder.Host.UseContentRoot(Directory.GetCurrentDirectory());
            builder.Host.UseDefaultServiceProvider((context, options) =>
                options.ValidateScopes = context.HostingEnvironment.IsDevelopment());

            builder.Logging.ClearProviders();
            if (Enum.TryParse(hostingConfig["LogLevel"], out LogLevel logLevel))
            {
                Console.WriteLine($"Console Logging enabled with level '{logLevel}'");
                builder.Logging.AddConsole();
                builder.Logging.SetMinimumLevel(logLevel);
            }

            var consoleArgs = new ConsoleArgs(args);
            var scenariosConfiguration = new ConsoleHostScenariosConfiguration(consoleArgs);
            var scenarios = new Scenarios(scenariosConfiguration);

            builder.Services.AddSingleton(consoleArgs);
            builder.Services.AddSingleton<IScenariosConfiguration>(scenariosConfiguration);
            builder.Services.AddSingleton(scenarios);
            builder.Services.Configure<LoggerFilterOptions>(options =>
            {
                if (Boolean.TryParse(hostingConfig["DisableScopes"], out var disableScopes) && disableScopes)
                {
                    Console.WriteLine($"LoggerFilterOptions.CaptureScopes = false");
                    options.CaptureScopes = false;
                }
            });

            builder.Services.Configure<AppSettings>(builder.Configuration);

            // Common DB services
            builder.Services.AddSingleton<IRandom, DefaultRandom>();

            var appSettings = builder.Configuration.Get<AppSettings>();
            BatchUpdateString.DatabaseServer = appSettings.Database;

            Console.WriteLine($"Database: {appSettings.Database}");
            Console.WriteLine($"ConnectionString: {appSettings.ConnectionString}");

            switch (appSettings.Database)
            {
                case DatabaseServer.PostgreSql:
                    builder.Services.AddEntityFrameworkNpgsql();
                    var pgSettings = new NpgsqlConnectionStringBuilder(appSettings.ConnectionString);
                    if (!pgSettings.NoResetOnClose && !pgSettings.Multiplexing)
                    {
                        throw new ArgumentException("No Reset On Close=true Or Multiplexing=true implies must be specified for Npgsql");
                    }
                    if (pgSettings.Enlist)
                    {
                        throw new ArgumentException("Enlist=false must be specified for Npgsql");
                    }

                    builder.Services.AddDbContextPool<ApplicationDbContext>(
                        options => options
                            .UseNpgsql(appSettings.ConnectionString,
                                o => o.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d)))
                            .EnableThreadSafetyChecks(false),
                        1024);

                    if (scenarios.Any("Raw") || scenarios.Any("Dapper"))
                    {
                        builder.Services.AddSingleton<DbProviderFactory>(NpgsqlFactory.Instance);
                    }
                    break;

                case DatabaseServer.SqlServer:
                    builder.Services.AddEntityFrameworkSqlServer();
                    builder.Services.AddDbContextPool<ApplicationDbContext>(options => options.UseSqlServer(appSettings.ConnectionString));

                    if (scenarios.Any("Raw") || scenarios.Any("Dapper"))
                    {
                        builder.Services.AddSingleton<DbProviderFactory>(SqlClientFactory.Instance);
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

                    builder.Services.AddEntityFrameworkSqlite();
                    builder.Services.AddDbContextPool<ApplicationDbContext>(options => options.UseSqlite(appSettings.ConnectionString));

                    if (scenarios.Any("Raw") || scenarios.Any("Dapper"))
                    {
                        builder.Services.AddSingleton<DbProviderFactory>(SqliteFactory.Instance);
                    }
                    break;
            }

            if (scenarios.Any("Ef"))
            {
                builder.Services.AddScoped<EfDb>();
            }

            if (scenarios.Any("Raw"))
            {
                builder.Services.AddScoped<RawDb>();
            }

            if (scenarios.Any("Dapper"))
            {
                builder.Services.AddScoped<DapperDb>();
            }

            if (scenarios.Any("Fortunes"))
            {
                var encoderSettings = new TextEncoderSettings(UnicodeRanges.BasicLatin, UnicodeRanges.Katakana, UnicodeRanges.Hiragana);
                encoderSettings.AllowCharacter('\u2014');  // allow EM DASH through
                builder.Services.AddWebEncoders(options =>
                {
                    options.TextEncoderSettings = encoderSettings;
                });
            }

            if (scenarios.Any("Endpoint"))
            {
                builder.Services.AddRouting();
            }

            if (scenarios.Any("Mvc"))
            {
                if (scenarios.MvcViews || scenarios.Any("MvcDbFortunes"))
                {
                    builder.Services.AddControllersWithViews();
                }
                else
                {
                    builder.Services.AddControllers();
                }
            }

            if (scenarios.Any("MemoryCache"))
            {
                builder.Services.AddMemoryCache();
            }

            if (scenarios.Any("ResponseCaching"))
            {
                builder.Services.AddResponseCaching();
            }

            if (String.Equals(Server, "Kestrel", StringComparison.OrdinalIgnoreCase))
            {
                builder.WebHost.UseKestrel(options =>
                {
                    var urls = hostingConfig["urls"] ?? hostingConfig["server.urls"];

                    if (!string.IsNullOrEmpty(urls))
                    {
                        foreach (var value in urls.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            Listen(options, hostingConfig, value);
                        }
                    }
                    else
                    {
                        Listen(options, hostingConfig, "http://localhost:5000/");
                    }
                });

                var threadCount = GetThreadCount(hostingConfig);

                builder.WebHost.UseSockets(socketOptions =>
                {
                    if (threadCount > 0)
                    {
                        socketOptions.IOQueueCount = threadCount;
                    }

                    Console.WriteLine($"Using Sockets with {socketOptions.IOQueueCount} threads");
                });

                builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
            }
            else if (String.Equals(Server, "HttpSys", StringComparison.OrdinalIgnoreCase))
            {
                // Disable cross-platform warning
#pragma warning disable CA1416
                builder.WebHost.UseHttpSys();
#pragma warning restore CA1416
            }
            else if (String.Equals(Server, "IISInProcess", StringComparison.OrdinalIgnoreCase))
            {
#if NET9_0_OR_GREATER
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_IIS_VERSION")))
                {
                    throw new InvalidOperationException("Benchmark wants to use IIS but app isn't running in IIS.");
                }
#endif
                builder.WebHost.UseKestrel().UseIIS();
            }
            else if (String.Equals(Server, "IISOutOfProcess", StringComparison.OrdinalIgnoreCase))
            {
#if NET9_0_OR_GREATER
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_IIS_VERSION")))
                {
                    throw new InvalidOperationException("Benchmark wants to use IIS but app isn't running in IIS.");
                }
#endif
                builder.WebHost.UseKestrel().UseIISIntegration();
            }
            else
            {
                throw new InvalidOperationException($"Unknown server value: {Server}");
            }

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            if (scenarios.StaticFiles)
            {
                app.UseStaticFiles();
            }

            if (scenarios.StaticFiles || scenarios.Plaintext)
            {
                app.UsePlainText();
            }

            if (scenarios.Json)
            {
                app.UseJson();
            }

            if (scenarios.CopyToAsync)
            {
                app.UseCopyToAsync();
            }

            // Single query endpoints
            if (scenarios.DbSingleQueryRaw)
            {
                app.UseSingleQueryRaw();
            }

            if (scenarios.DbSingleQueryDapper)
            {
                app.UseSingleQueryDapper();
            }

            if (scenarios.DbSingleQueryEf)
            {
                app.UseSingleQueryEf();
            }

            // Multiple query endpoints
            if (scenarios.DbMultiQueryRaw)
            {
                app.UseMultipleQueriesRaw();
            }

            if (scenarios.DbMultiQueryDapper)
            {
                app.UseMultipleQueriesDapper();
            }

            if (scenarios.DbMultiQueryEf)
            {
                app.UseMultipleQueriesEf();
            }

            // Multiple update endpoints
            if (scenarios.DbMultiUpdateRaw)
            {
                app.UseMultipleUpdatesRaw();
            }

            if (scenarios.DbMultiUpdateDapper)
            {
                app.UseMultipleUpdatesDapper();
            }

            if (scenarios.DbMultiUpdateEf)
            {
                app.UseMultipleUpdatesEf();
            }

            // Fortunes endpoints
            if (scenarios.DbFortunesRaw)
            {
                app.UseFortunesRaw();
            }

            if (scenarios.DbFortunesRawSync)
            {
                app.UseFortunesRawSync();
            }

            if (scenarios.DbFortunesDapper)
            {
                app.UseFortunesDapper();
            }

            if (scenarios.DbFortunesEf)
            {
                app.UseFortunesEf();
            }

            if (scenarios.Any("EndpointPlaintext"))
            {
                var helloWorldPayload = Encoding.UTF8.GetBytes("Hello, World!");

                app.MapGet("/ep-plaintext", async context =>
                {
                    var payloadLength = helloWorldPayload.Length;
                    var response = context.Response;
                    response.StatusCode = 200;
                    response.ContentType = "text/plain";
                    response.ContentLength = payloadLength;
                    await response.Body.WriteAsync(helloWorldPayload, 0, payloadLength);
                });
            }

            if (scenarios.Any("Mvc"))
            {
                app.MapControllers();
            }

            if (scenarios.MemoryCachePlaintext)
            {
                app.UseMemoryCachePlaintext();
            }

            if (scenarios.MemoryCachePlaintextSetRemove)
            {
                app.UseMemoryCachePlaintextSetRemove();
            }

            if (scenarios.ResponseCachingPlaintextCached)
            {
                app.UseResponseCachingPlaintextCached();
            }

            if (scenarios.ResponseCachingPlaintextResponseNoCache)
            {
                app.UseResponseCachingPlaintextResponseNoCache();
            }

            if (scenarios.ResponseCachingPlaintextRequestNoCache)
            {
                app.UseResponseCachingPlaintextRequestNoCache();
            }

            if (scenarios.ResponseCachingPlaintextVaryByCached)
            {
                app.UseResponseCachingPlaintextVaryByCached();
            }

            app.UseAutoShutdown();

            Console.WriteLine($"Using server {Server}");
            Console.WriteLine($"Server GC is currently {(GCSettings.IsServerGC ? "ENABLED" : "DISABLED")}");

            var nonInteractiveValue = hostingConfig["nonInteractive"];
            if (nonInteractiveValue == null || !bool.Parse(nonInteractiveValue))
            {
                StartInteractiveConsoleThread();
            }

            app.Run();
        }

        private static void StartInteractiveConsoleThread()
        {
            // Run the interaction on a separate thread as we don't have Console.KeyAvailable on .NET Core so can't
            // do a pre-emptive check before we call Console.ReadKey (which blocks, hard)

            var started = new ManualResetEvent(false);

            var interactiveThread = new Thread(() =>
            {
                Console.WriteLine("Press 'C' to force GC or any other key to display GC stats");
                Console.WriteLine();

                started.Set();

                while (true)
                {
                    var key = Console.ReadKey(intercept: true);

                    if (key.Key == ConsoleKey.C)
                    {
                        Console.WriteLine();
                        Console.Write("Forcing GC...");
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        Console.WriteLine(" done!");
                    }
                    else
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Allocated: {GetAllocatedMemory()}");
                        Console.WriteLine($"Gen 0: {GC.CollectionCount(0)}, Gen 1: {GC.CollectionCount(1)}, Gen 2: {GC.CollectionCount(2)}");
                    }
                }
            })
            {
                IsBackground = true
            };

            interactiveThread.Start();

            started.WaitOne();
        }

        private static string GetAllocatedMemory(bool forceFullCollection = false)
        {
            double bytes = GC.GetTotalMemory(forceFullCollection);

            return $"{((bytes / 1024d) / 1024d).ToString("N2")} MB";
        }

        private static int GetThreadCount(IConfigurationRoot config)
        {
            var threadCountValue = config["threadCount"];
            return threadCountValue == null ? -1 : int.Parse(threadCountValue);
        }

        private static void Listen(KestrelServerOptions options, IConfigurationRoot config, string url)
        {
            var urlPrefix = UrlPrefix.Create(url);
            var endpoint = CreateIPEndPoint(urlPrefix);

            options.Listen(endpoint, listenOptions =>
            {
                if (Protocol.Equals("h2", StringComparison.OrdinalIgnoreCase))
                {
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                }
                else if (Protocol.Equals("h2c", StringComparison.OrdinalIgnoreCase))
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                }

                if (urlPrefix.IsHttps)
                {
                    // [SuppressMessage("Microsoft.Security", "CSCAN0220.DefaultPasswordContexts", Justification="Benchmark code, not a secret")]
                    listenOptions.UseHttps("testCert.pfx", "testPassword");
                }
            });
        }

        private static IPEndPoint CreateIPEndPoint(UrlPrefix urlPrefix)
        {
            IPAddress ip;

            if (string.Equals(urlPrefix.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                ip = IPAddress.Loopback;
            }
            else if (!IPAddress.TryParse(urlPrefix.Host, out ip))
            {
                ip = IPAddress.IPv6Any;
            }

            return new IPEndPoint(ip, urlPrefix.PortValue);
        }
    }
}
