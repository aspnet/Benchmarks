// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Benchmarks.ServerJob;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Packaging;
using Repository;

namespace BenchmarkServer
{
    public class Startup
    {
        private const string _benchmarksRepoUrl = "https://github.com/aspnet/benchmarks.git";
        private static readonly Source _benchmarksSource = new Source() { Repository = _benchmarksRepoUrl };

        private const string _defaultUrl = "http://*:5001";
        private static readonly string _defaultHostname = Environment.MachineName.ToLowerInvariant();

        private static readonly IRepository<ServerJob> _jobs = new InMemoryRepository<ServerJob>();
        private static readonly bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();

            services.AddSingleton(_jobs);
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseMvc();
        }

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                Name = "BenchmarksServer",
                FullName = "ASP.NET Benchmark Server",
                Description = "REST APIs to run ASP.NET benchmark server"
            };

            app.HelpOption("-?|-h|--help");

            var urlOption = app.Option("-u|--url", $"URL for Rest APIs.  Default is '{_defaultUrl}'.",
                CommandOptionType.SingleValue);
            var hostnameOption = app.Option("-n|--hostname", $"Hostname for benchmark server.  Default is '{_defaultHostname}'.",
                CommandOptionType.SingleValue);

            app.OnExecute(() =>
            {
                var url = urlOption.HasValue() ? urlOption.Value() : _defaultUrl;
                var hostname = hostnameOption.HasValue() ? hostnameOption.Value() : _defaultHostname;
                return Run(url, hostname).Result;
            });

            return app.Execute(args);
        }

        private static async Task<int> Run(string url, string hostname)
        {
            var hostTask = Task.Run(() =>
            {
                var host = new WebHostBuilder()
                    .UseKestrel()
                    .UseStartup<Startup>()
                    .UseUrls(url)
                    .Build();

                host.Run();
            });

            var processJobsCts = new CancellationTokenSource();
            var processJobsTask = ProcessJobs(hostname, processJobsCts.Token);

            var completedTask = await Task.WhenAny(hostTask, processJobsTask);

            // Propagate exception (and exit process) if either task faulted
            await completedTask;

            // Host exited normally, so cancel job processor
            processJobsCts.Cancel();
            await processJobsTask;

            return 0;
        }

        private static async Task ProcessJobs(string hostname, CancellationToken cancellationToken)
        {
            Process process = null;

            string tempDir = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                var allJobs = _jobs.GetAll();
                var job = allJobs.FirstOrDefault();
                if (job != null)
                {
                    if (job.State == ServerState.Waiting)
                    {
                        // TODO: Race condition if DELETE is called during this code
                        try
                        {
                            if (!_isWindows && job.WebHost != WebHost.Kestrel)
                            {
                                Log.WriteLine($"Skipping job '{job.Id}' with scenario '{job.Scenario}'.");
                                Log.WriteLine($"'{job.WebHost}' is not supported on this platform.");
                                job.State = ServerState.NotSupported;
                                continue;
                            }

                            Log.WriteLine($"Starting job '{job.Id}' with scenario '{job.Scenario}'");
                            job.State = ServerState.Starting;

                            Debug.Assert(tempDir == null);
                            tempDir = GetTempDir();

                            var benchmarksDir = CloneRestoreBuild(tempDir, job);

                            Debug.Assert(process == null);
                            process = StartProcess(hostname, Path.Combine(tempDir, benchmarksDir), job);
                        }
                        catch (Exception e)
                        {
                            Log.WriteLine($"Error starting job '{job.Id}': {e}");

                            if (tempDir != null)
                            {
                                DeleteDir(tempDir);
                                tempDir = null;
                            }

                            job.State = ServerState.Failed;
                            continue;
                        }
                    }
                    else if (job.State == ServerState.Deleting)
                    {
                        Log.WriteLine($"Deleting job '{job.Id}' with scenario '{job.Scenario}'");

                        if (process != null)
                        {
                            // TODO: Replace with managed xplat version of kill process tree
                            if (_isWindows)
                            {
                                ProcessUtil.Run("taskkill.exe", $"/f /t /pid {process.Id}", throwOnError: false);
                            }
                            else
                            {
                                ProcessUtil.Run("pkill", "--signal SIGINT --full Benchmarks.dll", throwOnError: false);
                            }
                            process.Dispose();
                            process = null;
                        }

                        if (tempDir != null)
                        {
                            DeleteDir(tempDir);
                            tempDir = null;
                        }

                        _jobs.Remove(job.Id);
                    }
                }
                await Task.Delay(100);
            }
        }

        private static string CloneRestoreBuild(string path, ServerJob job)
        {
            // It's possible that the user specified a custom branch/commit for the benchmarks repo,
            // so we need to add that to the set of sources to restore if it's not already there.
            //
            // Note that this is also going to de-dupe the repos if the same one was specified twice at
            // the command-line (last first to support overrides).
            var repos = job.Sources.Append(_benchmarksSource).Distinct(SourceRepoComparer.Instance);

            // Clone
            string benchmarksDir = null;
            var dirs = new List<string>();
            foreach (var source in repos)
            {
                var dir = Git.Clone(path, source.Repository);
                if (SourceRepoComparer.Instance.Equals(source, _benchmarksSource))
                {
                    benchmarksDir = dir;
                }

                if (!string.IsNullOrEmpty(source.BranchOrCommit))
                {
                    Git.Checkout(Path.Combine(path, dir), source.BranchOrCommit);
                }
                dirs.Add(dir);
            }

            Debug.Assert(benchmarksDir != null);

            var builtDirs = new List<string>();
            var builtPackages = new List<BuiltPackage>();

            foreach (var dir in dirs.Except(new[] { benchmarksDir }))
            {
                UpdateNuGetConfig(path, dir, builtDirs);
                UpdateDependencies(path, dir, builtPackages);

                ProcessUtil.Run("dotnet", "restore", workingDirectory: Path.Combine(path, dir));
                ProcessUtil.Run("cmd", "/c build.cmd build-pack", workingDirectory: Path.Combine(path, dir),
                    environmentVariables: new Dictionary<string, string>
                    {
                        ["KOREBUILD_SKIP_RUNTIME_INSTALL"] = "1"
                    });

                builtDirs.Add(dir);

                builtPackages.AddRange(Directory.EnumerateFiles(Path.Combine(path, dir, "artifacts", "build"), "*.nupkg")
                    .Select(packageFile => new PackageArchiveReader(packageFile))
                    .Select(packageReader =>
                    {
                        var builtPackage = new BuiltPackage(
                            name: packageReader.NuspecReader.GetId(),
                            version: packageReader.NuspecReader.GetVersion().ToFullString(),
                            targetFrameworks: packageReader.NuspecReader.GetDependencyGroups().Select(dependencyGroup => dependencyGroup.TargetFramework.Framework));

                        packageReader.Dispose();

                        return builtPackage;
                    }));
            }

            UpdateNuGetConfig(path, benchmarksDir, builtDirs);
            UpdateProjectDependencies(Path.Combine(path, benchmarksDir, "src", "Benchmarks", "Benchmarks.csproj"), builtPackages);            
            ProcessUtil.Run("dotnet", "restore", workingDirectory: Path.Combine(path, benchmarksDir));

            return benchmarksDir;
        }

        private static void UpdateNuGetConfig(string path, string dir, IEnumerable<string> builtDirs)
        {
            var nugetConfig = Path.Combine(path, dir, "NuGet.config");
            var nugetConfigDoc = XDocument.Load(nugetConfig);
            var packageSources = nugetConfigDoc.Root.Element("packageSources");

            foreach (var builtDir in builtDirs)
            {
                packageSources.AddFirst(new XElement("add", new[]
                {
                    new XAttribute("key", builtDir),
                    new XAttribute("value", Path.Combine(path, builtDir, "artifacts", "build").Replace(@"\", @"\\"))
                }));
            }

            using (var writer = XmlWriter.Create(nugetConfig, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
            {
                nugetConfigDoc.Save(writer);
            }
        }

        private static void UpdateDependencies(string path, string dir, IEnumerable<BuiltPackage> builtPackages)
        {
            foreach (var srcProject in Directory.EnumerateFiles(Path.Combine(path, dir, "src"), "*.csproj", SearchOption.AllDirectories))
            {
                UpdateProjectDependencies(srcProject, builtPackages);
            }
        }

        private static void UpdateProjectDependencies(string srcProject, IEnumerable<BuiltPackage> builtPackages)
        {
            var projectDocument = XDocument.Load(srcProject);

            foreach (var reference in projectDocument.Descendants("PackageReference").ToList())
            {
                if (builtPackages.Any(package => package.Name == reference.Attribute("Include").Value))
                {
                    reference.Remove();
                }
            }

            var commonReferences = new XElement("ItemGroup");
            var netFrameworkReferences = new XElement("ItemGroup", new XAttribute("Condition", @" '$(TargetFramework)' == 'net451' "));
            var netCoreReferences = new XElement("ItemGroup", new XAttribute("Condition", @" '$(TargetFramework)' == 'netcoreapp1.1' "));

            foreach (var builtPackage in builtPackages)
            {
                var reference = new XElement("PackageReference", new XAttribute[]
                {
                    new XAttribute("Include", builtPackage.Name),
                    new XAttribute("Version", builtPackage.Version)
                });

                if (builtPackage.TargetFrameworks.Count() > 1)
                {
                    commonReferences.Add(reference);
                }
                else if (builtPackage.TargetFrameworks.First().StartsWith(".NETFramework"))
                {
                    netFrameworkReferences.Add(reference);
                }
                else if (builtPackage.TargetFrameworks.First().StartsWith(".NETStandard"))
                {
                    netCoreReferences.Add(reference);
                }
                else
                {
                    // TODO
                }
            }

            projectDocument.Root.AddFirst(netCoreReferences);
            projectDocument.Root.AddFirst(netFrameworkReferences);
            projectDocument.Root.AddFirst(commonReferences);

            using (var writer = XmlWriter.Create(srcProject, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
            {
                projectDocument.Save(writer);
            }
        }

        private static string GetTempDir()
        {
            var temp = Path.GetTempFileName();
            File.Delete(temp);
            Directory.CreateDirectory(temp);

            Log.WriteLine($"Created temp directory '{temp}'");

            return temp;
        }

        private static void DeleteDir(string path)
        {
            Log.WriteLine($"Deleting directory '{path}'");

            // Delete occasionally fails with the following exception:
            //
            // System.UnauthorizedAccessException: Access to the path 'Benchmarks.dll' is denied.
            //
            // If delete fails, retry once every second up to 10 times.
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    var dir = new DirectoryInfo(path) { Attributes = FileAttributes.Normal };
                    foreach (var info in dir.GetFileSystemInfos("*", SearchOption.AllDirectories))
                    {
                        info.Attributes = FileAttributes.Normal;
                    }
                    dir.Delete(recursive: true);
                    break;
                }
                catch (Exception e)
                {
                    Log.WriteLine($"Error deleting directory: {e.ToString()}");

                    if (i < 9)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        private static Process StartProcess(string hostname, string benchmarksRepo, ServerJob job)
        {
            var filename = "dotnet";
            var arguments = $"run -c Release -f netcoreapp1.1 -- --scenarios {job.Scenario} --server {job.WebHost} " +
                $"--server.urls {job.Scheme.ToString().ToLowerInvariant()}://{hostname}:5000";

            if (!string.IsNullOrEmpty(job.ConnectionFilter))
            {
                arguments += $" --connectionFilter {job.ConnectionFilter}";
            }

            Log.WriteLine($"Starting process '{filename} {arguments}'");

            var process = new Process()
            {
                StartInfo = {
                    FileName = filename,
                    Arguments = arguments,
                    WorkingDirectory = Path.Combine(benchmarksRepo, "src", "Benchmarks"),
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
                EnableRaisingEvents = true
            };
            process.StartInfo.Environment.Add("COREHOST_SERVER_GC", "1");

            process.OutputDataReceived += (_, e) =>
            {
                if (e != null && e.Data != null)
                {
                    Log.WriteLine(e.Data);

                    if (job.State == ServerState.Starting && e.Data.Contains("Application started"))
                    {
                        job.State = ServerState.Running;
                        job.Url = ComputeServerUrl(hostname, job.Scheme, job.Scenario);
                        Log.WriteLine($"Running job '{job.Id}' with scenario '{job.Scenario}'");
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();

            return process;
        }

        private static string ComputeServerUrl(string hostname, Scheme scheme, Scenario scenario)
        {
            var scenarioName = scenario.ToString();
            var path = scenarioName;

            var field = scenario.GetType().GetTypeInfo().GetField(scenarioName);
            var pathAttribute = field.GetCustomAttribute<ScenarioPathAttribute>();
            if (pathAttribute != null)
            {
                path = pathAttribute.Paths.First().Trim('/');
            }

            return $"{scheme.ToString().ToLowerInvariant()}://{hostname}:5000/{path.ToLower()}";
        }

        private static string GetRepoName(Source source)
        {
            // Attempt to parse a string like
            // - http://<host>.com/<user>/<repo>.git OR
            // - http://<host>.com/<user>/<repo>
            var repository = source.Repository;
            var lastSlash = repository.LastIndexOf('/');
            var dot = repository.LastIndexOf('.');

            if (lastSlash == -1)
            {
                throw new InvalidOperationException($"Couldn't parse repository name from {source.Repository}");
            }

            var start = lastSlash + 1; // +1 to skip over the slash.
            var name = dot > lastSlash ? repository.Substring(start, dot - start) : repository.Substring(start);
            return name;
        }

        // Compares just the repository name
        private class SourceRepoComparer : IEqualityComparer<Source>
        {
            public static readonly SourceRepoComparer Instance = new SourceRepoComparer();

            public bool Equals(Source x, Source y)
            {
                return string.Equals(GetRepoName(x), GetRepoName(y), StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(Source obj)
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(GetRepoName(obj));
            }
        }

        private class BuiltPackage
        {
            public BuiltPackage(string name, string version, IEnumerable<string> targetFrameworks)
            {
                Name = name;
                Version = version;
                TargetFrameworks = targetFrameworks;
            }

            public string Name { get; }
            public string Version { get; }
            public IEnumerable<string> TargetFrameworks { get; }
        }
    }
}
