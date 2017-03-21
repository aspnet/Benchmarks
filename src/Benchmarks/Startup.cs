// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Common;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading;
using Benchmarks.Configuration;
using Benchmarks.Data;
using Benchmarks.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Npgsql;

namespace Benchmarks
{
    public class Startup
    {
        public Startup(IHostingEnvironment hostingEnv, Scenarios scenarios)
        {
            // Set up configuration sources.
            var builder = new ConfigurationBuilder()
                .SetBasePath(hostingEnv.ContentRootPath)
                .AddCommandLine(Program.Args)
                .AddJsonFile("appsettings.json")
                .AddJsonFile($"appsettings.{hostingEnv.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();

            Scenarios = scenarios;
        }

        public IConfigurationRoot Configuration { get; set; }

        public Scenarios Scenarios { get; }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.Configure<AppSettings>(Configuration);

            // We re-register the Scenarios as an instance singleton here to avoid it being created again due to the
            // registration done in Program.Main
            services.AddSingleton(Scenarios);

            // Common DB services
            services.AddSingleton<IRandom, DefaultRandom>();
            services.AddSingleton<ApplicationDbSeeder>();

            services
                .AddEntityFrameworkSqlServer()
                .AddDbContextPool<ApplicationDbContext>((sp, ob) =>
                {
                    var appSettings = sp.GetRequiredService<IOptions<AppSettings>>().Value;

                    if (appSettings.Database == DatabaseServer.PostgreSql)
                    {
                        ob.UseNpgsql(appSettings.ConnectionString);
                    }
                    else
                    {
                        ob.UseSqlServer(appSettings.ConnectionString);
                    }
                });

            if (Scenarios.StaticFiles)
            {
                AddCachedWebRoot(services);
            }

            if (Scenarios.Any("Raw") || Scenarios.Any("Dapper"))
            {
                services.AddSingleton<DbProviderFactory>((provider) => {
                    var settings = provider.GetRequiredService<IOptions<AppSettings>>().Value;

                    if (settings.Database == DatabaseServer.PostgreSql)
                    {
                        return NpgsqlFactory.Instance;
                    }
                    else
                    {
                        return SqlClientFactory.Instance;
                    }
                });
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

            if (Scenarios.Any("Mvc"))
            {
                var mvcBuilder = services
                    .AddMvcCore()
                    //.AddApplicationPart(typeof(Startup).GetTypeInfo().Assembly)
                    .AddControllersAsServices();

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

            if (Scenarios.Any("MemoryCache"))
            {
                services.AddMemoryCache();
            }

            if (Scenarios.Any("ResponseCaching"))
            {
                services.AddResponseCaching();
            }

            return services.BuildServiceProvider(validateScopes: true);
        }

        public void Configure(IApplicationBuilder app, ApplicationDbSeeder dbSeeder, IOptions<AppSettings> appSettings,
            ILoggerFactory loggerFactory)
        {
            if (appSettings.Value.LogLevel.HasValue)
            {
                loggerFactory.AddConsole(appSettings.Value.LogLevel.Value);
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

            if (Scenarios.Copy)
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

            if (Scenarios.DbFortunesDapper)
            {
                app.UseFortunesDapper();
            }

            if (Scenarios.DbFortunesEf)
            {
                app.UseFortunesEf();
            }

            if (Scenarios.Any("Db"))
            {
                if (!dbSeeder.Seed())
                {
                    Environment.Exit(1);
                }
            }

            if (Scenarios.Any("Mvc"))
            {
                app.UseMvc();
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

            app.RunDebugInfoPage();
        }

        private static void AddCachedWebRoot(IServiceCollection services)
        {
            services.AddSingleton<IStartupFilter, AppStartFilter>();

            // Turn off compaction on memory pressure as it results in things being evicted during the priming of the
            // cache on application start.
            services.AddMemoryCache(options => options.CompactOnMemoryPressure = false);
            services.AddSingleton<CachedWebRootFileProvider>();
            services.AddSingleton<IConfigureOptions<StaticFileOptions>, StaticFileOptionsSetup>();
        }

        private class StaticFileOptionsSetup : IConfigureOptions<StaticFileOptions>
        {
            private readonly CachedWebRootFileProvider _cachedWebRoot;

            public StaticFileOptionsSetup(CachedWebRootFileProvider cachedWebRoot)
            {
                _cachedWebRoot = cachedWebRoot;
            }

            public void Configure(StaticFileOptions options)
            {
                options.FileProvider = _cachedWebRoot;
            }
        }

        private class AppStartFilter : IStartupFilter
        {
            private readonly CachedWebRootFileProvider _cachedWebRoot;

            public AppStartFilter(CachedWebRootFileProvider cachedWebRoot)
            {
                _cachedWebRoot = cachedWebRoot;
            }

            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            {
                return app =>
                {
                    // Set usual stuff up.
                    next(app);

                    // Prime the cached file provider.
                    _cachedWebRoot.PrimeCache();
                };
            }
        }

        private class CachedWebRootFileProvider : IFileProvider
        {
            private static readonly int _fileSizeLimit = 256 * 1024; // bytes

            private readonly ILogger<CachedWebRootFileProvider> _logger;
            private readonly IFileProvider _fileProvider;
            private readonly IMemoryCache _cache;

            public CachedWebRootFileProvider(ILogger<CachedWebRootFileProvider> logger, IHostingEnvironment hostingEnv, IMemoryCache memoryCache)
            {
                _logger = logger;
                _fileProvider = hostingEnv.WebRootFileProvider;
                _cache = memoryCache;
            }

            public void PrimeCache()
            {
                var started = _logger.IsEnabled(LogLevel.Information) ? Timing.GetTimestamp() : 0;

                _logger.LogInformation("Priming the cache");
                var cacheSize = PrimeCacheImpl("/");

                if (started != 0)
                {
                    _logger.LogInformation("Cache primed with {cacheEntriesCount} entries totaling {cacheEntriesSizeBytes} bytes in {elapsed}", cacheSize.Item1, cacheSize.Item2, Timing.GetDuration(started));
                }
            }

            private Tuple<int, long> PrimeCacheImpl(string currentPath)
            {
                _logger.LogTrace("Priming cache for {currentPath}", currentPath);
                var cacheEntriesAdded = 0;
                var bytesCached = (long)0;

                // TODO: Normalize the currentPath here, e.g. strip/always-add leading slashes, ensure slash consistency, etc.
                var prefix = string.Equals(currentPath, "/", StringComparison.OrdinalIgnoreCase) ? "/" : currentPath + "/";

                foreach (var fileInfo in GetDirectoryContents(currentPath))
                {
                    if (fileInfo.IsDirectory)
                    {
                        var cacheSize = PrimeCacheImpl(prefix + fileInfo.Name);
                        cacheEntriesAdded += cacheSize.Item1;
                        bytesCached += cacheSize.Item2;
                    }
                    else
                    {
                        var stream = GetFileInfo(prefix + fileInfo.Name).CreateReadStream();
                        bytesCached += stream.Length;
                        stream.Dispose();
                        cacheEntriesAdded++;
                    }
                }

                return Tuple.Create(cacheEntriesAdded, bytesCached);
            }

            public IDirectoryContents GetDirectoryContents(string subpath)
            {
                // TODO: Normalize the subpath here, e.g. strip/always-add leading slashes, ensure slash consistency, etc.
                var key = nameof(GetDirectoryContents) + "_" + subpath;
                if (_cache.TryGetValue(key, out IDirectoryContents cachedResult))
                {
                    // Item already exists in cache, just return it
                    return cachedResult;
                }

                var directoryContents = _fileProvider.GetDirectoryContents(subpath);
                if (!directoryContents.Exists)
                {
                    // Requested subpath doesn't exist, just return
                    return directoryContents;
                }

                // Create the cache entry and return
                var cacheEntry = _cache.CreateEntry(key);
                cacheEntry.Value = directoryContents;
                cacheEntry.RegisterPostEvictionCallback((k, value, reason, s) =>
                    _logger.LogTrace("Cache entry {key} was evicted due to {reason}", k, reason));
                return directoryContents;
            }

            public IFileInfo GetFileInfo(string subpath)
            {
                // TODO: Normalize the subpath here, e.g. strip/always-add leading slashes, ensure slash consistency, etc.
                var key = nameof(GetFileInfo) + "_" + subpath;
                if (_cache.TryGetValue(key, out IFileInfo cachedResult))
                {
                    // Item already exists in cache, just return it
                    return cachedResult;
                }

                var fileInfo = _fileProvider.GetFileInfo(subpath);
                if (!fileInfo.Exists)
                {
                    // Requested subpath doesn't exist, just return it
                    return fileInfo;
                }

                if (fileInfo.Length > _fileSizeLimit)
                {
                    // File is too large to cache, just return it
                    _logger.LogTrace("File contents for {subpath} will not be cached as it's over the file size limit of {fileSizeLimit}", subpath, _fileSizeLimit);
                    return fileInfo;
                }

                // Create the cache entry and return
                var cachedFileInfo = new CachedFileInfo(_logger, fileInfo, subpath);
                var fileChangedToken = Watch(subpath);
                fileChangedToken.RegisterChangeCallback(_ => _logger.LogDebug("Change detected for {subpath} located at {filepath}", subpath, fileInfo.PhysicalPath), null);
                var cacheEntry = _cache.CreateEntry(key)
                    .RegisterPostEvictionCallback((k, value, reason, s) =>
                        _logger.LogTrace("Cache entry {key} was evicted due to {reason}", k, reason))
                    .AddExpirationToken(fileChangedToken)
                    .SetValue(cachedFileInfo);
                // You have to call Dispose() to actually add the item to the underlying cache. Yeah, I know.
                cacheEntry.Dispose();
                return cachedFileInfo;
            }

            public IChangeToken Watch(string filter)
            {
                return _fileProvider.Watch(filter);
            }

            private class CachedFileInfo : IFileInfo
            {
                private readonly ILogger _logger;
                private readonly IFileInfo _fileInfo;
                private readonly string _subpath;
                private byte[] _contents;

                public CachedFileInfo(ILogger logger, IFileInfo fileInfo, string subpath)
                {
                    _logger = logger;
                    _fileInfo = fileInfo;
                    _subpath = subpath;
                }

                public bool Exists => _fileInfo.Exists;

                public bool IsDirectory => _fileInfo.IsDirectory;

                public DateTimeOffset LastModified => _fileInfo.LastModified;

                public long Length => _fileInfo.Length;

                public string Name => _fileInfo.Name;

                public string PhysicalPath => _fileInfo.PhysicalPath;

                public Stream CreateReadStream()
                {
                    var contents = _contents;
                    if (contents != null)
                    {
                        _logger.LogTrace("Returning cached file contents for {subpath} located at {filepath}", _subpath, _fileInfo.PhysicalPath);
                        return new MemoryStream(contents);
                    }
                    else
                    {
                        _logger.LogTrace("Loading file contents for {subpath} located at {filepath}", _subpath, _fileInfo.PhysicalPath);
                        MemoryStream ms;
                        using (var fs = _fileInfo.CreateReadStream())
                        {
                            ms = new MemoryStream((int)fs.Length);
                            fs.CopyTo(ms);
                            contents = ms.ToArray();
                            ms.Position = 0;
                        }

                        if (Interlocked.CompareExchange(ref _contents, contents, null) == null)
                        {
                            _logger.LogTrace("Cached file contents for {subpath} located at {filepath}", _subpath, _fileInfo.PhysicalPath);
                        }

                        return ms;
                    }
                }
            }
        }

        private static class Timing
        {
            private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

            public static long GetTimestamp() => Stopwatch.GetTimestamp();

            public static TimeSpan GetDuration(long start) => GetDuration(start, Stopwatch.GetTimestamp());

            public static TimeSpan GetDuration(long start, long end) => new TimeSpan((long)(TimestampToTicks * (end - start)));
        }
    }
}
