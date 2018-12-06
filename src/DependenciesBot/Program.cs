using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Octokit;

namespace DependenciesBot
{
    class Program
    {
        static readonly string _extensionsPackageId = "Microsoft.Extensions.Caching.Memory";
        static readonly string _extensionsVersionPrefix = "3.0.0-preview.";

        // The packages to update in aspnet/Extensions
        static readonly HashSet<string> _extensionsPackageNames = new HashSet<string>()
        {
            "InternalAspNetCoreAnalyzersPackageVersion",
            "MicrosoftAspNetCoreAnalyzerTestingPackageVersion",
            "MicrosoftAspNetCoreBenchmarkRunnerSourcesPackageVersion",
            "MicrosoftAspNetCoreCertificatesGenerationSourcesPackageVersion",
            "MicrosoftAspNetCoreTestingPackageVersion",
            "MicrosoftExtensionsActivatorUtilitiesSourcesPackageVersion",
            "MicrosoftExtensionsCachingAbstractionsPackageVersion",
            "MicrosoftExtensionsCachingMemoryPackageVersion",
            "MicrosoftExtensionsCachingSqlServerPackageVersion",
            "MicrosoftExtensionsCachingStackExchangeRedisPackageVersion",
            "MicrosoftExtensionsClosedGenericMatcherSourcesPackageVersion",
            "MicrosoftExtensionsCommandLineUtilsSourcesPackageVersion",
            "MicrosoftExtensionsConfigurationAbstractionsPackageVersion",
            "MicrosoftExtensionsConfigurationAzureKeyVaultPackageVersion",
            "MicrosoftExtensionsConfigurationBinderPackageVersion",
            "MicrosoftExtensionsConfigurationCommandLinePackageVersion",
            "MicrosoftExtensionsConfigurationEnvironmentVariablesPackageVersion",
            "MicrosoftExtensionsConfigurationFileExtensionsPackageVersion",
            "MicrosoftExtensionsConfigurationIniPackageVersion",
            "MicrosoftExtensionsConfigurationJsonPackageVersion",
            "MicrosoftExtensionsConfigurationKeyPerFilePackageVersion",
            "MicrosoftExtensionsConfigurationPackageVersion",
            "MicrosoftExtensionsConfigurationUserSecretsPackageVersion",
            "MicrosoftExtensionsConfigurationXmlPackageVersion",
            "MicrosoftExtensionsCopyOnWriteDictionarySourcesPackageVersion",
            "MicrosoftExtensionsDependencyInjectionAbstractionsPackageVersion",
            "MicrosoftExtensionsDependencyInjectionPackageVersion",
            "MicrosoftExtensionsDependencyInjectionSpecificationTestsPackageVersion",
            "MicrosoftExtensionsDiagnosticAdapterPackageVersion",
            "MicrosoftExtensionsDiagnosticsHealthChecksAbstractionsPackageVersion",
            "MicrosoftExtensionsDiagnosticsHealthChecksPackageVersion",
            "MicrosoftExtensionsFileProvidersAbstractionsPackageVersion",
            "MicrosoftExtensionsFileProvidersCompositePackageVersion",
            "MicrosoftExtensionsFileProvidersEmbeddedPackageVersion",
            "MicrosoftExtensionsFileProvidersPhysicalPackageVersion",
            "MicrosoftExtensionsFileSystemGlobbingPackageVersion",
            "MicrosoftExtensionsHashCodeCombinerSourcesPackageVersion",
            "MicrosoftExtensionsHostingAbstractionsPackageVersion",
            "MicrosoftExtensionsHostingPackageVersion",
            "MicrosoftExtensionsHttpPackageVersion",
            "MicrosoftExtensionsLocalizationAbstractionsPackageVersion",
            "MicrosoftExtensionsLocalizationPackageVersion",
            "MicrosoftExtensionsLoggingAbstractionsPackageVersion",
            "MicrosoftExtensionsLoggingAzureAppServicesPackageVersion",
            "MicrosoftExtensionsLoggingConfigurationPackageVersion",
            "MicrosoftExtensionsLoggingConsolePackageVersion",
            "MicrosoftExtensionsLoggingDebugPackageVersion",
            "MicrosoftExtensionsLoggingEventSourcePackageVersion",
            "MicrosoftExtensionsLoggingPackageVersion",
            "MicrosoftExtensionsLoggingTestingPackageVersion",
            "MicrosoftExtensionsLoggingTraceSourcePackageVersion",
            "MicrosoftExtensionsNonCapturingTimerSourcesPackageVersion",
            "MicrosoftExtensionsObjectMethodExecutorSourcesPackageVersion",
            "MicrosoftExtensionsObjectPoolPackageVersion",
            "MicrosoftExtensionsOptionsConfigurationExtensionsPackageVersion",
            "MicrosoftExtensionsOptionsDataAnnotationsPackageVersion",
            "MicrosoftExtensionsOptionsPackageVersion",
            "MicrosoftExtensionsParameterDefaultValueSourcesPackageVersion",
            "MicrosoftExtensionsPrimitivesPackageVersion",
            "MicrosoftExtensionsProcessSourcesPackageVersion",
            "MicrosoftExtensionsPropertyActivatorSourcesPackageVersion",
            "MicrosoftExtensionsPropertyHelperSourcesPackageVersion",
            "MicrosoftExtensionsRazorViewsSourcesPackageVersion",
            "MicrosoftExtensionsSecurityHelperSourcesPackageVersion",
            "MicrosoftExtensionsStackTraceSourcesPackageVersion",
            "MicrosoftExtensionsTypeNameHelperSourcesPackageVersion",
            "MicrosoftExtensionsValueStopwatchSourcesPackageVersion",
            "MicrosoftExtensionsWebEncodersPackageVersion",
            "MicrosoftExtensionsWebEncodersSourcesPackageVersion",
        };

        static readonly string _efCorePackageId = "Microsoft.EntityFrameworkCore.Abstractions";
        static readonly string _efCoreVersionPrefix = "3.0.0-preview.";

        // The packages to update in aspnet/EntityFrameworkCore
        static readonly HashSet<string> _efCorePackageNames = new HashSet<string>()
        {
            "MicrosoftEntityFrameworkCoreAbstractionsPackageVersion",
            "MicrosoftEntityFrameworkCoreAnalyzersPackageVersion",
            "MicrosoftEntityFrameworkCoreDesignPackageVersion",
            "MicrosoftEntityFrameworkCoreInMemoryPackageVersion",
            "MicrosoftEntityFrameworkCoreRelationalPackageVersion",
            "MicrosoftEntityFrameworkCoreSqlitePackageVersion",
            "MicrosoftEntityFrameworkCoreSqlServerPackageVersion",
            "MicrosoftEntityFrameworkCoreToolsPackageVersion",
            "MicrosoftEntityFrameworkCorePackageVersion",
        };

        // extensions/efcore/aspnetcore
        static readonly string _extensionsDependencies = "https://raw.githubusercontent.com/aspnet/Extensions/master/eng/Dependencies.props";
        static readonly string _efCoreDependencies = "https://raw.githubusercontent.com/aspnet/EntityFrameworkCore/master/build/dependencies.props";
        static readonly string _aspnetCoreDependencies = "https://raw.githubusercontent.com/aspnet/AspNetCore/master/build/dependencies.props";

        static readonly string _extensionsDependenciesFilename = "extensions-dependencies.props";
        static readonly string _efcoreDependenciesFilename = "efcore-dependencies.props";
        static readonly string _aspnetCoreDependenciesFilename = "aspnetcore-dependencies.props";

        // core-setup/corefx
        static readonly string _latestCoreSetupPackages = "https://raw.githubusercontent.com/dotnet/versions/master/build-info/dotnet/core-setup/master/Latest_Packages.txt";
        static readonly string _latestCoreFxPackages = "https://raw.githubusercontent.com/dotnet/versions/master/build-info/dotnet/corefx/master/Latest_Packages.txt";
        static readonly string _coreSetupCoherence = "https://raw.githubusercontent.com/dotnet/core-setup/master/dependencies.props";

        static readonly string _coreSetupCoherenceFilename = "core-setup-dependencies.props";
        static readonly string _latestCoreSetupPackagesFilename = "core-setup-latest.txt";
        static readonly string _latestCoreFxPackagesFilename = "corefx-latest.txt";

        static readonly HashSet<string> _ignorePackages = new HashSet<string> ()
        {
            "SystemValueTuplePackageVersion",
            "SystemMemoryPackageVersion",
            "MicrosoftNETFrameworkCompatibilityPackageVersion",
            "SystemBuffersPackageVersion",
            "SystemIOPipesAccessControlPackageVersion",
            "SystemWindowsExtensionsPackageVersion",
        };

        static readonly HttpClient _httpClient = new HttpClient();

        static ProductHeaderValue _productHeaderValue = new ProductHeaderValue("BenchmarksBot");
        static string _accessToken;
        static string _username;
        static long _repositoryId;

        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix: "DEPENDENCIESBOT_")
                .AddCommandLine(args)
                .Build();

            LoadSettings(config);

            var action = args[0];

            if (action == "clean")
            {
                await Clean();

                return;
            }

            if (action == "coherence")
            {
                await EnsureCoherence();

                return;
            }

            if (action == "extensions")
            {
                await UpdateExtensionsDependencies();

                return;
            }

            if (action == "efcore")
            {
                await UpdateEfCoreDependencies();

                return;
            }

            if (action == "aspnetcore")
            {
                await UpdateAspNetCoreDependencies();

                return;
            }

            Console.WriteLine("Expected argument: coherence, extensions, efcore, aspnetcore");
        }

        private static void Clean()
        {
            File.Delete(_coreSetupCoherenceFilename);
            File.Delete(_latestCoreSetupPackagesFilename);
            File.Delete(_latestCoreFxPackagesFilename);
            File.Delete(_extensionsDependenciesFilename);
            File.Delete(_efcoreDependenciesFilename);
        }

        private static async Task<bool> EnsureCoherence()
        {
            // Ensure the corefx package versions are coherent with the version of MicrosoftNETCoreAppPackageVersion. 
            // If the corefx package versions are newer, we'll end up with extra assemblies in our shared framework.
            // We ensure that core-setup has built with new corefx packages yet:
            // In  for 
            // MicrosoftNETCoreRuntimeCoreCLRPackageVersion and MicrosoftNETCorePlatformsPackageVersion are matching the Latest built packages

            // MicrosoftNETCoreRuntimeCoreCLRPackageVersion 3.0.0-preview-27207-02 (https://raw.githubusercontent.com/dotnet/core-setup/master/dependencies.props)
            // Microsoft.NETCore.App 3.0.0-preview-27206-02 (https://raw.githubusercontent.com/dotnet/versions/master/build-info/dotnet/core-setup/master/Latest_Packages.txt)

            // MicrosoftNETCorePlatformsPackageVersion 3.0.0-preview.18606.1 (https://raw.githubusercontent.com/dotnet/core-setup/master/dependencies.props)
            // Microsoft.NETCore.Platforms 3.0.0-preview.18606.1 (https://raw.githubusercontent.com/dotnet/versions/master/build-info/dotnet/corefx/master/Latest_Packages.txt)

            // Delete the files from the previous workflow

            if(
            File.Exists(_coreSetupCoherenceFilename)
            || File.Exists(_latestCoreSetupPackagesFilename)
            || File.Exists(_latestCoreFxPackagesFilename)
            || File.Exists(_extensionsDependenciesFilename)
            || File.Exists(_efcoreDependenciesFilename))
            {
                Log("Existing files were found. Use 'clean' before 'extensions' to start a new workflow.");

                return false;
            }

            Log("# Checking coherence");

            var success = 
                await DownloadFileAsync(_coreSetupCoherence, _coreSetupCoherenceFilename)
                && await DownloadFileAsync(_latestCoreSetupPackages, _latestCoreSetupPackagesFilename)
                && await DownloadFileAsync(_latestCoreFxPackages, _latestCoreFxPackagesFilename);

            if (!success)
            {
                return false;
            }

            var coreSetupCoherence = await File.ReadAllTextAsync(_coreSetupCoherenceFilename);

            var expectedCoreSetupVersion = new Regex($@"\<MicrosoftNETCoreRuntimeCoreCLRPackageVersion\>([\w\-\.]+)\</MicrosoftNETCoreRuntimeCoreCLRPackageVersion\>").Match(coreSetupCoherence).Groups[1].Value;
            var expectedCoreFxVersion = new Regex($@"\<MicrosoftNETCorePlatformsPackageVersion\>([\w\-\.]+)\</MicrosoftNETCorePlatformsPackageVersion\>").Match(coreSetupCoherence).Groups[1].Value;

            var latestCoreSetup = await File.ReadAllTextAsync(_latestCoreSetupPackagesFilename);
            var latestCoreFx = await File.ReadAllTextAsync(_latestCoreFxPackagesFilename);

            var latestCoreSetupVersion = new Regex(@"^Microsoft\.NETCore\.App\s+([\w\-\.]+)\s*$", RegexOptions.Multiline).Match(latestCoreSetup).Groups[1].Value;
            var latestCoreFxVersion = new Regex(@"^Microsoft\.NETCore\.Platforms\s+([\w\-\.]+)\s*$", RegexOptions.Multiline).Match(latestCoreFx).Groups[1].Value;

            if (expectedCoreSetupVersion != latestCoreSetupVersion || expectedCoreFxVersion != latestCoreFxVersion)
            {
                Log($"Core-Setup: {expectedCoreSetupVersion}");
                Log($"CoreFx: {expectedCoreFxVersion}");

                Log($"SUCCESS");

                return true;
            }
            else
            {
                Log($"Core-Setup: {expectedCoreSetupVersion} / {latestCoreSetupVersion}");
                Log($"CoreFx: {expectedCoreFxVersion} / {latestCoreFxVersion}");

                return false;
            }
        }

        private static void Log(string text)
        {
            Console.WriteLine("[{0}] {1}", DateTime.UtcNow.ToShortTimeString(), text);
        }

        private static async Task<string> PatchCoreSetupCoreFxVersionAsync(string deps)
        {
            foreach (var source in new[] { _latestCoreSetupPackagesFilename, _latestCoreFxPackagesFilename })
            {
                // Load latest dependencies from core-setup
                var latestPackages = await File.ReadAllTextAsync(source);

                using (var sr = new StringReader(latestPackages))
                {
                    var line = sr.ReadLine();

                    while (!String.IsNullOrEmpty(line))
                    {
                        var parts = line.Split(' ');

                        if (parts.Length != 2)
                        {
                            throw new ApplicationException($"Expected 2 parts in latest core-setup packages: {line}");
                        }

                        var packageName = parts[0];
                        var packageVersion = parts[1];

                        var normalizedPackageName = packageName.Replace(".", "") + "PackageVersion";

                        // Performance is not a concern
                        var oldDependency = new Regex($@"\<{normalizedPackageName}\>([\w\-\.]+)\</{normalizedPackageName}\>");

                        // Search for this package in the existing dependencies
                        var match = oldDependency.Match(deps);

                        if (match.Success)
                        {
                            var oldPackageVersion = match.Groups[1].Value;

                            if (oldPackageVersion != packageVersion)
                            {
                                if (!_ignorePackages.Contains(normalizedPackageName))
                                {
                                    if (oldPackageVersion != packageVersion)
                                    {
                                        Console.WriteLine($"[Core-Setup/CoreFx] Updated {normalizedPackageName} {oldPackageVersion} -> {packageVersion}");
                                        deps = deps.Replace(
                                            $"<{normalizedPackageName}>{oldPackageVersion}</{normalizedPackageName}>",
                                            $"<{normalizedPackageName}>{packageVersion}</{normalizedPackageName}>");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"Skipped {normalizedPackageName} {oldPackageVersion} -> {packageVersion}");
                                }
                            }
                        }

                        line = sr.ReadLine();
                    }
                }
            }

            return deps;
        }

        private static async Task UpdateAspNetCoreDependencies()
        {
            // Load existing dependencies 
            var source = await DownloadContentAsync(_aspnetCoreDependencies);

            // Apply version changes from core-setup and corefx
            var deps = await PatchCoreSetupCoreFxVersionAsync(source);

            // The versions in aspnet/Extensions are updated at that point to overwrite any from core-setup/corefx
            // This is done such that AspNetCore and Extensions use the same versions

            var extensionsDeps = await File.ReadAllTextAsync(_extensionsDependenciesFilename);

            var extensionsDoc = XDocument.Parse(extensionsDeps);

            var allPackageElements = extensionsDoc.Root.Elements("PropertyGroup").SelectMany(x => x.Elements());

            foreach (var el in allPackageElements)
            {
                var normalizedPackageName = el.Name.ToString();
                var packageVersion = el.Value;

                // Performance is not a concern
                var oldDependency = new Regex($@"\<{normalizedPackageName}\>([\w\-\.]+)\</{normalizedPackageName}\>");

                // Search for this package in the existing dependencies
                var match = oldDependency.Match(deps);

                if (match.Success)
                {
                    var oldPackageVersion = match.Groups[1].Value;

                    if (!_ignorePackages.Contains(normalizedPackageName))
                    {
                        if (oldPackageVersion != packageVersion)
                        {
                            Console.WriteLine($"[Extensions deps] Updated {normalizedPackageName} {oldPackageVersion} -> {packageVersion}");
                            deps = deps.Replace(
                                $"<{normalizedPackageName}>{oldPackageVersion}</{normalizedPackageName}>",
                                $"<{normalizedPackageName}>{packageVersion}</{normalizedPackageName}>");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Skipped {normalizedPackageName} {oldPackageVersion} -> {packageVersion}");
                    }
                }
            }

            // Seek the latest built Extension packages
            var extensionsMygetVersion = await GetLatestAspNetCoreMygetVersion(_extensionsPackageId, _extensionsVersionPrefix);

            foreach (var normalizedPackageName in _extensionsPackageNames)
            {
                // Performance is not a concern
                var oldDependency = new Regex($@"\<{normalizedPackageName}\>([\w\-\.]+)\</{normalizedPackageName}\>");

                // Search for this package in the existing dependencies
                var match = oldDependency.Match(deps);

                if (match.Success)
                {
                    var oldPackageVersion = match.Groups[1].Value;

                    if (!_ignorePackages.Contains(normalizedPackageName))
                    {
                        if (oldPackageVersion != extensionsMygetVersion)
                        {
                            Console.WriteLine($"[Extensions package] Updated {normalizedPackageName} {oldPackageVersion} -> {extensionsMygetVersion}");
                            deps = deps.Replace(
                                $"<{normalizedPackageName}>{oldPackageVersion}</{normalizedPackageName}>",
                                $"<{normalizedPackageName}>{extensionsMygetVersion}</{normalizedPackageName}>");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Skipped {normalizedPackageName} {oldPackageVersion} -> {extensionsMygetVersion}");
                    }
                }
            }

            // The versions in aspnet/EntityFramework are updated at that point to overwrite any from core-setup/corefx
            // This is done such that AspNetCore and Extensions use the same versions

            var efCoreDeps = await File.ReadAllTextAsync(_efcoreDependenciesFilename);

            var efCoreDoc = XDocument.Parse(efCoreDeps);

            var allEfCorePackageElements = efCoreDoc.Root.Elements("PropertyGroup").SelectMany(x => x.Elements());

            foreach (var el in allEfCorePackageElements)
            {
                var normalizedPackageName = el.Name.ToString();
                var packageVersion = el.Value;

                // Performance is not a concern
                var oldDependency = new Regex($@"\<{normalizedPackageName}\>([\w\-\.]+)\</{normalizedPackageName}\>");

                // Search for this package in the existing dependencies
                var match = oldDependency.Match(deps);

                if (match.Success)
                {
                    var oldPackageVersion = match.Groups[1].Value;

                    if (!_ignorePackages.Contains(normalizedPackageName))
                    {
                        if (oldPackageVersion != packageVersion)
                        {
                            if (oldPackageVersion != packageVersion)
                            {
                                Console.WriteLine($"[EntityFrameworkCore deps] Updated {normalizedPackageName} {oldPackageVersion} -> {packageVersion}");
                                deps = deps.Replace(
                                    $"<{normalizedPackageName}>{oldPackageVersion}</{normalizedPackageName}>",
                                    $"<{normalizedPackageName}>{packageVersion}</{normalizedPackageName}>");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Skipped {normalizedPackageName} {oldPackageVersion} -> {packageVersion}");
                    }
                }
            }

            // Seek the latest built EntityFramework packages

            var efCoreMygetVersion = await GetLatestAspNetCoreMygetVersion(_efCorePackageId, _efCoreVersionPrefix);

            foreach (var normalizedPackageName in _efCorePackageNames)
            {
                // Performance is not a concern
                var oldDependency = new Regex($@"\<{normalizedPackageName}\>([\w\-\.]+)\</{normalizedPackageName}\>");

                // Search for this package in the existing dependencies
                var match = oldDependency.Match(deps);

                if (match.Success)
                {
                    var oldPackageVersion = match.Groups[1].Value;

                    if (!_ignorePackages.Contains(normalizedPackageName))
                    {
                        if (oldPackageVersion != efCoreMygetVersion)
                        {
                            Console.WriteLine($"[EntityFrameworkCore package] Updated {normalizedPackageName} {oldPackageVersion} -> {efCoreMygetVersion}");
                            deps = deps.Replace(
                                $"<{normalizedPackageName}>{oldPackageVersion}</{normalizedPackageName}>",
                                $"<{normalizedPackageName}>{efCoreMygetVersion}</{normalizedPackageName}>");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Skipped {normalizedPackageName} {oldPackageVersion} -> {efCoreMygetVersion}");
                    }
                }
            }

            File.WriteAllText(_aspnetCoreDependenciesFilename, deps);
        }

        private static async Task UpdateEfCoreDependencies()
        {
            await DownloadFileAsync(_efCoreDependencies, _efcoreDependenciesFilename);

            var source = await File.ReadAllTextAsync(_efcoreDependenciesFilename);

            // Apply version changes from core-setup and corefx
            var deps = await PatchCoreSetupCoreFxVersionAsync(source);

            // The versions in aspnet/Extensions are updated at that point to overwrite any from core-setup/corefx
            // This is done such that EfCore and Extensions use the same versions

            var extensionsDeps = await File.ReadAllTextAsync(_extensionsDependenciesFilename);

            var extensionsDoc = XDocument.Parse(extensionsDeps);

            var allPackageElements = extensionsDoc.Root.Elements("PropertyGroup").SelectMany(x => x.Elements());

            foreach (var el in allPackageElements)
            {
                var normalizedPackageName = el.Name.ToString();
                var packageVersion = el.Value;

                // Performance is not a concern
                var oldDependency = new Regex($@"\<{normalizedPackageName}\>([\w\-\.]+)\</{normalizedPackageName}\>");

                // Search for this package in the existing dependencies
                var match = oldDependency.Match(deps);

                if (match.Success)
                {
                    var oldPackageVersion = match.Groups[1].Value;

                    if (!_ignorePackages.Contains(normalizedPackageName))
                    {
                        if (oldPackageVersion != packageVersion)
                        {
                            Console.WriteLine($"[Extensions deps] Updated {normalizedPackageName} {oldPackageVersion} -> {packageVersion}");
                            deps = deps.Replace(
                                $"<{normalizedPackageName}>{oldPackageVersion}</{normalizedPackageName}>",
                                $"<{normalizedPackageName}>{packageVersion}</{normalizedPackageName}>");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Skipped {normalizedPackageName} {oldPackageVersion} -> {packageVersion}");
                    }
                }
            }


            // Seek the latest built Extension packages
            var extensionsMygetVersion = await GetLatestAspNetCoreMygetVersion(_extensionsPackageId, _extensionsVersionPrefix);

            foreach(var normalizedPackageName in _extensionsPackageNames)
            {
                // Performance is not a concern
                var oldDependency = new Regex($@"\<{normalizedPackageName}\>([\w\-\.]+)\</{normalizedPackageName}\>");

                // Search for this package in the existing dependencies
                var match = oldDependency.Match(deps);

                if (match.Success)
                {
                    var oldPackageVersion = match.Groups[1].Value;

                    if (!_ignorePackages.Contains(normalizedPackageName))
                    {
                        if (oldPackageVersion != extensionsMygetVersion)
                        {
                            Console.WriteLine($"[Extensions package] Updated {normalizedPackageName} {oldPackageVersion} -> {extensionsMygetVersion}");
                            deps = deps.Replace(
                                $"<{normalizedPackageName}>{oldPackageVersion}</{normalizedPackageName}>",
                                $"<{normalizedPackageName}>{extensionsMygetVersion}</{normalizedPackageName}>");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Skipped {normalizedPackageName} {oldPackageVersion} -> {extensionsMygetVersion}");
                    }
                }
            }

            File.WriteAllText(_efcoreDependenciesFilename, deps);
        }

        private static async Task UpdateExtensionsDependencies()
        {
            // Load existing dependencies file from aspnet/Extensions
            await DownloadFileAsync(_extensionsDependencies, _extensionsDependenciesFilename);

            var source = await File.ReadAllTextAsync(_extensionsDependenciesFilename);

            var deps = await PatchCoreSetupCoreFxVersionAsync(source);

            File.WriteAllText(_extensionsDependenciesFilename, deps);

            Log("SUCCESS");
        }

        private static void LoadSettings(IConfiguration config)
        {
            // Tip: The repository id van be found using this endpoint: https://api.github.com/repos/aspnet/Benchmarks

            long.TryParse(config["RepositoryId"], out _repositoryId);
            _accessToken = config["AccessToken"];
            _username = config["Username"];

            if (String.IsNullOrEmpty(_accessToken))
            {
                throw new ArgumentException("AccessToken argument is missing");
            }

            if (String.IsNullOrEmpty(_username))
            {
                throw new ArgumentException("BotUsername argument is missing");
            }
        }

        private static async Task<string> GetLatestAspNetCoreMygetVersion(string packageId, string versionPrefix)
        {
            var index = JObject.Parse(await DownloadContentAsync($"https://dotnet.myget.org/F/aspnetcore-dev/api/v3/registration1/{packageId}/index.json"));

            var compatiblePages = index["items"].Where(t => ((string)t["lower"]).StartsWith(versionPrefix));
            var last = compatiblePages.Any() ? compatiblePages.Last() : index["items"].Last;
            var lastPageUrl = (string)last["@id"];

            var lastPage = JObject.Parse(await DownloadContentAsync(lastPageUrl));

            // Extract the highest version
            var lastVersion = (string)lastPage["items"]
                .Where(t => ((string)t["catalogEntry"]["version"]).StartsWith(versionPrefix)).Last()
                ["catalogEntry"]["version"]
                ;


            return lastVersion;
        }

        //private static async Task CreateIssue(IEnumerable<Regression> regressions)
        //{
        //    if (regressions == null || !regressions.Any())
        //    {
        //        return;
        //    }

        //    var client = new GitHubClient(_productHeaderValue);
        //    client.Credentials = new Credentials(_accessToken);

        //    var body = new StringBuilder();
        //    body.Append("A performance regression has been detected for the following scenarios:");

        //    foreach (var r in regressions.OrderBy(x => x.Scenario).ThenBy(x => x.DateTimeUtc))
        //    {
        //        body.AppendLine();
        //        body.AppendLine();
        //        body.AppendLine("| Scenario | Environment | Date | Old RPS | New RPS | Change | Deviation |");
        //        body.AppendLine("| -------- | ----------- | ---- | ------- | ------- | ------ | --------- |");

        //        var prevRPS = r.Values.Skip(2).First();
        //        var rps = r.Values.Last();
        //        var change = Math.Round((double)(rps - prevRPS) / prevRPS * 100, 2);
        //        var deviation = Math.Round((double)(rps - prevRPS) / r.Stdev, 2);

        //        body.AppendLine($"| {r.Scenario} | {r.OperatingSystem}, {r.Scheme}, {r.WebHost} | {r.DateTimeUtc.ToString("u")} | {prevRPS.ToString("n0")} | {rps.ToString("n0")} | {change} % | {deviation} σ |");


        //        body.AppendLine();
        //        body.AppendLine("Before versions:");

        //        body.AppendLine($"Microsoft.AspNetCore.App __{r.PreviousAspNetCoreVersion}__");
        //        body.AppendLine($"Microsoft.NetCore.App __{r.PreviousRuntimeVersion}__");

        //        body.AppendLine();
        //        body.AppendLine("After versions:");

        //        body.AppendLine($"Microsoft.AspNetCore.App __{r.CurrentAspNetCoreVersion}__");
        //        body.AppendLine($"Microsoft.NetCore.App __{r.CurrentRuntimeVersion}__");

        //        var aspNetChanged = r.PreviousAspNetCoreVersion != r.CurrentAspNetCoreVersion;
        //        var runtimeChanged = r.PreviousRuntimeVersion != r.CurrentRuntimeVersion;

        //        if (aspNetChanged || runtimeChanged)
        //        {
        //            body.AppendLine();
        //            body.AppendLine("Commits:");

        //            if (aspNetChanged)
        //            {
        //                if (r.AspNetCoreHashes != null && r.AspNetCoreHashes.Length == 2 && r.AspNetCoreHashes[0] != null && r.AspNetCoreHashes[1] != null)
        //                {
        //                    body.AppendLine();
        //                    body.AppendLine("__Microsoft.AspNetCore.App__");
        //                    body.AppendLine($"https://github.com/aspnet/AspNetCore/compare/{r.AspNetCoreHashes[0]}...{r.AspNetCoreHashes[1]}");
        //                }
        //            }

        //            if (runtimeChanged)
        //            {
        //                if (r.CoreFxHashes != null && r.CoreFxHashes.Length == 2 && r.CoreFxHashes[0] != null && r.CoreFxHashes[1] != null)
        //                {
        //                    body.AppendLine();
        //                    body.AppendLine("__Microsoft.NetCore.App / Core FX__");
        //                    body.AppendLine($"https://github.com/dotnet/corefx/compare/{r.CoreFxHashes[0]}...{r.CoreFxHashes[1]}");
        //                }

        //                if (r.CoreClrHashes != null && r.CoreClrHashes.Length == 2 && r.CoreClrHashes[0] != null && r.CoreClrHashes[1] != null)
        //                {
        //                    body.AppendLine();
        //                    body.AppendLine("__Microsoft.NetCore.App / Core CLR__");
        //                    body.AppendLine($"https://github.com/dotnet/coreclr/compare/{r.CoreClrHashes[0]}...{r.CoreClrHashes[1]}");
        //                }
        //            }
        //        }
        //    }

        //    var title = "Performance regression: " + String.Join(", ", regressions.Select(x => x.Scenario).Take(5));

        //    if (regressions.Count() > 5)
        //    {
        //        title += " ...";
        //    }

        //    var createIssue = new NewIssue(title)
        //    {
        //        Body = body.ToString()
        //    };

        //    createIssue.Labels.Add("perf-regression");

        //    Console.Write(createIssue.Body);

        //    var issue = await client.Issue.Create(_repositoryId, createIssue);
        //}

        private static async Task<bool> DownloadFileAsync(string url, string outputPath, int maxRetries = 3, int timeout = 5)
        {
            Log($"Downloading '{url}'");

            for (var i = 0; i < maxRetries; ++i)
            {
                try
                {
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
                    var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);
                    response.EnsureSuccessStatusCode();

                    // This probably won't use async IO on windows since the stream
                    // needs to created with the right flags
                    using (var stream = File.Create(outputPath))
                    {
                        // Copy the response stream directly to the file stream
                        await response.Content.CopyToAsync(stream);
                    }

                    return true;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while downloading {url}:");
                    Console.WriteLine(e);
                }
            }

            return false;
        }

        private static async Task<string> DownloadContentAsync(string url, int maxRetries = 3, int timeout = 5)
        {
            for (var i = 0; i < maxRetries; ++i)
            {
                try
                {
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
                    return await _httpClient.GetStringAsync(url);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while downloading {url}:");
                    Console.WriteLine(e);
                }
            }

            throw new ApplicationException($"Error while downloading {url} after {maxRetries} attempts");
        }
    }
}
