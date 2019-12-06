// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Benchmarks.ServerJob;
using Fluid;
using Fluid.Values;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json.Linq;

namespace BenchmarksDriver
{
    public class Program
    {
        private static TimeSpan _timeout = TimeSpan.FromMinutes(5);

        private static readonly HttpClient _httpClient;
        private static readonly HttpClientHandler _httpClientHandler;

        private static string _tableName = "AspNetBenchmarks";
        private const string EventPipeOutputFile = "eventpipe.netperf";

        // Default to arguments which should be sufficient for collecting trace of default Plaintext run
        private const string _defaultTraceArguments = "BufferSizeMB=1024;CircularMB=1024;clrEvents=JITSymbols;kernelEvents=process+thread+ImageLoad+Profile";

        private static CommandOption
            _outputArchiveOption,
            _buildArchiveOption,
            _buildFileOption,
            _outputFileOption,
            _initializeOption,
            _noStartupLatencyOption,
            
            _serverRuntimeVersionOption,
            _clientRuntimeVersionOption,
            _serverAspnetCoreVersionOption,
            _clientAspnetCoreVersionOption,

            _configOption,
            _profileOption

            ;

        private static HashSet<string> DynamicArguments = new HashSet<string>() { "services", "variables", "dependencies" };

        // The dynamic arguments that will alter the configurations
        private static List<KeyValuePair<string, string>> Arguments = new List<KeyValuePair<string, string>>();

        private static Dictionary<string, string> _deprecatedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "--projectfile", "--project-file" },
            { "--outputfile", "--output-file" },
            { "--clientName", "--client-name" }
        };

        private static Dictionary<string, string> _synonymArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "--aspnet", "--aspnetcoreversion" },
            { "--runtime", "--runtimeversion" },
            { "--clientThreads", "--client-threads" },
        };

        static Program()
        {
            // Configuring the http client to trust the self-signed certificate
            _httpClientHandler = new HttpClientHandler();
            _httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            _httpClientHandler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            _httpClient = new HttpClient(_httpClientHandler);

            TemplateContext.GlobalMemberAccessStrategy.Register<JObject, object>((obj, name) => obj[name]);
            FluidValue.SetTypeMapping<JObject>(o => new ObjectValue(o));
            FluidValue.SetTypeMapping<JValue>(o => FluidValue.Create(((JValue)o).Value));
            FluidValue.SetTypeMapping<DateTime>(o => new ObjectValue(o));
        }

        public static int Main(string[] args)
        {
            // Replace deprecated arguments with new ones
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (_deprecatedArguments.TryGetValue(arg, out var mappedArg))
                {
                    Log.Write($"WARNING: '{arg}' has been deprecated, in the future please use '{mappedArg}'.");
                    args[i] = mappedArg;
                }
                else if (_synonymArguments.TryGetValue(arg, out var synonymArg))
                {
                    // We don't need to display a warning
                    args[i] = synonymArg;
                }
            }

            var app = new CommandLineApplication()
            {
                Name = "BenchmarksDriver",
                FullName = "ASP.NET Benchmark Driver",
                Description = "Driver for ASP.NET Benchmarks",
                ResponseFileHandling = ResponseFileHandling.ParseArgsAsSpaceSeparated,
                OptionsComparison = StringComparison.OrdinalIgnoreCase
            };

            app.HelpOption("-?|-h|--help");

            // New options

            _configOption = app.Option("--config", "Configuration file or url", CommandOptionType.MultipleValue);
            _profileOption = app.Option("--profile", "Profile to execute", CommandOptionType.SingleValue);

            
            // Extract dynamic arguments
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                foreach (var dynamicArgument in DynamicArguments)
                {
                    if (arg.StartsWith("--" + dynamicArgument))
                    {
                        // Remove this argument from the command line
                        args[i] = "";

                        // Dynamic arguments always come in pairs 
                        if (i + 1 < args.Length)
                        {
                            Arguments.Add(KeyValuePair.Create(arg.Substring(2), args[i + 1]));
                            args[i+1] = "";

                            i++;
                        }
                    }
                }
            }

            // Driver Options
            var sqlConnectionStringOption = app.Option("-q|--sql",
                "Connection string of SQL Database to store results", CommandOptionType.SingleValue);
            var sqlTableOption = app.Option("-t|--table",
                "Table name of the SQL Database to store results", CommandOptionType.SingleValue);
            var verboseOption = app.Option("-v|--verbose",
                "Verbose output", CommandOptionType.NoValue);
            var quietOption = app.Option("--quiet",
                "Quiet output, only the results are displayed", CommandOptionType.NoValue);
            var sessionOption = app.Option("--session",
                "A logical identifier to group related jobs.", CommandOptionType.SingleValue);
            var descriptionOption = app.Option("--description",
                "The description of the job.", CommandOptionType.SingleValue);
            var iterationsOption = app.Option("-i|--iterations",
                "The number of iterations.", CommandOptionType.SingleValue);
            var excludeOption = app.Option("-x|--exclude",
                "The number of best and worst jobs to skip.", CommandOptionType.SingleValue);
            var shutdownOption = app.Option("--before-shutdown",
                "An endpoint to call before the application has shut down.", CommandOptionType.SingleValue);
            var spanOption = app.Option("-sp|--span",
                "The time during which the client jobs are repeated, in 'HH:mm:ss' format. e.g., 48:00:00 for 2 days.", CommandOptionType.SingleValue);
            var markdownOption = app.Option("-md|--markdown",
                "Formats the output in markdown", CommandOptionType.NoValue);
            var writeToFileOption = app.Option("-wf|--write-file",
                "Writes the results to a file", CommandOptionType.NoValue);
            var windowsOnlyOption = app.Option("--windows-only",
                "Don't execute the job if the server is not running on Windows", CommandOptionType.NoValue);
            var linuxOnlyOption = app.Option("--linux-only",
                "Don't execute the job if the server is not running on Linux", CommandOptionType.NoValue);
            var saveOption = app.Option("--save",
                "Stores the results in a local file, e.g. --save baseline. If the extension is not specified, '.bench.json' is used.", CommandOptionType.SingleValue);
            var diffOption = app.Option("--diff",
                "Displays the results of the run compared to a previously saved result, e.g. --diff baseline. If the extension is not specified, '.bench.json' is used.", CommandOptionType.SingleValue);
            var benchmarkdotnetOption = app.Option("--benchmarkdotnet",
                "Runs a BenchmarkDotNet application, with an optional filter. e.g., --benchmarkdotnet, --benchmarkdotnet:*MyBenchmark*", CommandOptionType.SingleOrNoValue);
            var consoleOption = app.Option("--console",
                "Runs the benchmarked application as a console application, such that no client is used and its output is displayed locally.", CommandOptionType.NoValue);

            // ServerJob Options
            var databaseOption = app.Option("--database",
                "The type of database to run the benchmarks with (PostgreSql, SqlServer or MySql). Default is None.", CommandOptionType.SingleValue);
            var kestrelThreadCountOption = app.Option("--kestrelThreadCount",
                "Maps to KestrelServerOptions.ThreadCount.",
                CommandOptionType.SingleValue);
            var serverScenarioOption = app.Option("--server-scenario",
                "Server scenario to run", CommandOptionType.SingleValue);
            var clientScenarioOption = app.Option("--client-scenario",
                "Client scenario to run", CommandOptionType.SingleValue);
            var schemeOption = app.Option("-m|--scheme",
                "Scheme (http, https, h2, h2c). Default is http.", CommandOptionType.SingleValue);
            var webHostOption = app.Option(
                "-w|--webHost",
                "WebHost (e.g., KestrelLibuv, KestrelSockets, HttpSys). Default is KestrelSockets.",
                CommandOptionType.SingleValue);
            _serverAspnetCoreVersionOption = app.Option("--server-aspnet-version",
                "ASP.NET Core packages version on the server (Current, Latest, or custom value). Current is the latest public version (2.0.*), Latest is the currently developed one. Default is Latest (2.2-*).", CommandOptionType.SingleValue);
            _clientAspnetCoreVersionOption = app.Option("--client-aspnet-version",
                "ASP.NET Core packages version on the server (Current, Latest, or custom value). Current is the latest public version (2.0.*), Latest is the currently developed one. Default is Latest (2.2-*).", CommandOptionType.SingleValue);
            _serverRuntimeVersionOption = app.Option("--server-runtime-version",
                ".NET Core Runtime version on the server (Current, Latest, Edge or custom value). Current is the latest public version, Latest is the one enlisted, Edge is the latest available. Default is Latest (2.2.0-*).", CommandOptionType.SingleValue);
            _clientRuntimeVersionOption = app.Option("--client-runtime-version",
                ".NET Core Runtime version on the client (Current, Latest, Edge or custom value). Current is the latest public version, Latest is the one enlisted, Edge is the latest available. Default is Latest (2.2.0-*).", CommandOptionType.SingleValue);
            var serverNoArgumentsOptions = app.Option("--server-no-arguments",
                "Removes any predefined arguments from the server application command line.", CommandOptionType.NoValue);
            var clientNoArgumentsOptions = app.Option("--client-no-arguments",
                "Removes any predefined arguments from the client application command line.", CommandOptionType.NoValue);
            var portOption = app.Option("--port",
                "The port used to request the benchmarked application. Default is 5000.", CommandOptionType.SingleValue);
            var readyTextOption = app.Option("--ready-text",
                "The text that is displayed when the application is ready to accept requests. (e.g., \"Application started.\")", CommandOptionType.SingleValue);
            var repositoryOption = app.Option("-r|--repository",
                "Git repository containing the project to test.", CommandOptionType.SingleValue);
            var dockerFileOption = app.Option("-df|--docker-file",
                "File path of the Docker script. (e.g, \"frameworks/CSharp/aspnetcore/aspcore.dockerfile\")", CommandOptionType.SingleValue);
            var dockerContextOption = app.Option("-dc|--docker-context",
                "Docker context directory. Defaults to the Docker file directory. (e.g., \"frameworks/CSharp/aspnetcore/\")", CommandOptionType.SingleValue);
            var dockerImageOption = app.Option("-di|--docker-image",
                "The name of the Docker image to create. If not net one will be created from the Docker file name. (e.g., \"aspnetcore21\")", CommandOptionType.SingleValue);
            var useRuntimeStoreOption = app.Option("--runtime-store",
                "Runs the benchmarks using the runtime store (2.0) or shared aspnet framework (2.1).", CommandOptionType.NoValue);
            _outputFileOption = app.Option("--output-file",
                "Output file attachment. Format is 'path[;destination]'. Path can be a URL. e.g., " +
                "\"--output-file c:\\build\\Microsoft.AspNetCore.Mvc.dll\", " +
                "\"--output-file c:\\files\\samples\\picture.png;wwwroot\\picture.png\"",
                CommandOptionType.MultipleValue);
            _buildFileOption = app.Option("--build-file",
                "Build file attachment. Format is 'path[;destination]'. Path can be a URL. e.g., " +
                "\"--build-file c:\\build\\Microsoft.AspNetCore.Mvc.dll\", " +
                "\"--build-file c:\\files\\samples\\picture.png;wwwroot\\picture.png\"",
                CommandOptionType.MultipleValue);
            _outputArchiveOption = app.Option("--output-archive",
                "Output archive attachment. Format is 'path[;destination]'. Path can be a URL. e.g., " +
                "\"--output-archive c:\\build\\Microsoft.AspNetCore.Mvc.zip\", " +
                "\"--output-archive http://raw/github.com/pictures.zip;wwwroot\\pictures\"",
                CommandOptionType.MultipleValue);
            _buildArchiveOption = app.Option("--build-archive",
                "Build archive attachment. Format is 'path[;destination]'. Path can be a URL. e.g., " +
                "\"--build-archive c:\\build\\Microsoft.AspNetCore.Mvc.zip\", " +
                "\"--build-archive http://raw/github.com/pictures.zip;wwwroot\\pictures\"",
                CommandOptionType.MultipleValue);
            var buildArguments = app.Option("-ba|--build-arg",
                "Defines custom build arguments to use with the benchmarked application e.g., -b \"/p:foo=bar\" --build-arg \"quiet\"", CommandOptionType.MultipleValue);
            var serverTimeoutOption = app.Option("--server-timeout",
                "Timeout for server jobs. e.g., 00:05:00", CommandOptionType.SingleValue);
            _initializeOption = app.Option("--initialize",
                "A script to run before the application starts, e.g. \"du\", \"/usr/bin/env bash dotnet-install.sh\"", CommandOptionType.SingleValue);

            app.OnExecute(() =>
            {

                Log.IsQuiet = quietOption.HasValue();
                Log.IsVerbose = verboseOption.HasValue();

                if (serverTimeoutOption.HasValue())
                {
                    TimeSpan.TryParse(serverTimeoutOption.Value(), out _timeout);
                }

                var schemeValue = schemeOption.Value();
                if (string.IsNullOrEmpty(schemeValue))
                {
                    schemeValue = "http";
                }

                var webHostValue = webHostOption.Value();
                if (string.IsNullOrEmpty(webHostValue))
                {
                    webHostValue = "KestrelSockets";
                }

                var session = sessionOption.Value();
                if (String.IsNullOrEmpty(session))
                {
                    session = Guid.NewGuid().ToString("n");
                }

                var description = descriptionOption.Value() ?? "";

                if (iterationsOption.HasValue() && spanOption.HasValue())
                {
                    Console.WriteLine($"The options --iterations and --span can't be used together.");

                    app.ShowHelp();
                    return 10;
                }

                var iterations = 1;
                var exclude = 0;

                var sqlConnectionString = sqlConnectionStringOption.Value();
                var span = TimeSpan.Zero;

                if (sqlTableOption.HasValue())
                {
                    _tableName = sqlTableOption.Value();
                }

                var profileName = _profileOption.Value();

                var configuration = LoadConfiguration(_configOption.Values, profileName, Arguments);

                Benchmarks.ServerJob.OperatingSystem? requiredOperatingSystem = null;

                if (windowsOnlyOption.HasValue())
                {
                    requiredOperatingSystem = Benchmarks.ServerJob.OperatingSystem.Windows;
                }

                if (linuxOnlyOption.HasValue())
                {
                    requiredOperatingSystem = Benchmarks.ServerJob.OperatingSystem.Linux;
                }

                // Verifying endpoints

                foreach (var dependency in configuration.Dependencies)
                {
                    var service = configuration.Services[dependency];

                    foreach(var endpoint in service.Endpoints)
                    {
                        try
                        {
                            using (var cts = new CancellationTokenSource(2000))
                            {
                                var response = _httpClient.GetAsync(endpoint, cts.Token).Result;
                                response.EnsureSuccessStatusCode();
                            }
                        }
                        catch
                        {
                            Console.WriteLine($"The specified endpoint url '{endpoint}' for '{service}' is invalid or not responsive.");
                            return 2;
                        }
                    }
                }

                return Run(
                    configuration,
                    sqlConnectionString,
                    session,
                    description,
                    iterations,
                    exclude,
                    shutdownOption.Value(),
                    span,
                    //scriptFileOption,
                    markdownOption,
                    writeToFileOption,
                    requiredOperatingSystem,
                    saveOption,
                    diffOption
                    ).Result;
            });

            // Resolve response files from urls

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("@http", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var tempFilename = Path.GetTempFileName();

                        var filecontent = _httpClient.GetStringAsync(args[i].Substring(1)).GetAwaiter().GetResult();
                        File.WriteAllText(tempFilename, filecontent);
                        args[i] = "@" + tempFilename;
                    }
                    catch
                    {
                        Console.WriteLine($"Invalid reponse file url '{args[i].Substring(1)}'");
                        return -1;
                    }
                }
            }

            try
            {
                return app.Execute(args.Where(x => !String.IsNullOrEmpty(x)).ToArray());
            }
            catch (CommandParsingException e)
            {
                Console.WriteLine();
                Console.WriteLine(e.Message);
                return -1;
            }
            finally
            {
                // TODO: clean the files for all jobs
                //CleanTemporaryFiles();
            }
        }

        private static async Task<int> Run(
            Configuration configuration,
            string sqlConnectionString,
            string session,
            string description,
            int iterations,
            int exclude,
            string shutdownEndpoint,
            TimeSpan span,
            //List<string> downloadFiles,
            //bool fetch,
            //string fetchDestination,
            //bool collectR2RLog,
            // string traceDestination,
            // CommandOption scriptFileOption,
            CommandOption markdownOption,
            CommandOption writeToFileOption,
            Benchmarks.ServerJob.OperatingSystem? requiredOperatingSystem,
            CommandOption saveOption,
            CommandOption diffOption
            )
        {
            var results = new List<Statistics>();

            Log.Write($"Running session '{session}' with description '{description}'");

            for (var i = 1; i <= iterations; i++)
            {
                if (iterations > 1)
                {
                    Log.Write($"Job {i} of {iterations}");
                }

                var jobsByDependency = new Dictionary<string, List<JobConnection>>();

                foreach (var dependency in configuration.Dependencies)
                {
                    var service = configuration.Services[dependency];
                    service.DriverVersion = 2;

                    var jobs = service.Endpoints.Select(endpoint => new JobConnection(service, new Uri(endpoint))).ToList();

                    jobsByDependency.Add(dependency, jobs);

                    var variables = MergeVariables(configuration.Variables, service.Variables);

                    // Format arguments

                    if (FluidTemplate.TryParse(service.Arguments, out var template))
                    {
                        var context = new TemplateContext();

                        foreach(var property in variables)
                        {
                            context.SetValue(property.Key, property.Value);
                        }

                        service.Arguments = template.Render(context);
                    }

                    // Start this group of jobs
                    await Task.WhenAll(
                        jobs.Select(job =>
                        {
                            // Start server
                            return job.StartAsync(
                                requiredOperatingSystem?.ToString(),
                                _outputArchiveOption,
                                _buildArchiveOption,
                                _outputFileOption,
                                _buildFileOption
                            );
                        })
                    );

                    foreach (var job in jobs)
                    {
                        job.StartKeepAlive();
                    }

                    if (service.WaitForExit)
                    {
                        // Wait for all clients to stop
                        while (!jobs.All(client => client.Job.State != ServerState.Running))
                        {
                            // Refresh the local state
                            foreach (var job in jobs)
                            {
                                await job.TryUpdateStateAsync();
                            }

                            await Task.Delay(1000);
                        }
                    }
                }

                // Download traces, before the jobs are stopped
                foreach (var dependency in configuration.Dependencies)
                {
                    var service = configuration.Services[dependency];

                    var jobConnections = jobsByDependency[dependency];

                    foreach (var jobConnection in jobConnections)
                    {
                        // Download trace
                        if (jobConnection.Job.DotNetTrace)
                        {
                            try
                            {
                                var traceDestination = jobConnection.Job.Options.TraceOutput;

                                if (String.IsNullOrWhiteSpace(traceDestination))
                                {
                                    traceDestination = dependency;
                                }

                                var traceExtension = ".nettrace";

                                if (!traceDestination.EndsWith(traceExtension, StringComparison.OrdinalIgnoreCase))
                                {
                                    traceDestination = traceDestination + "." + DateTime.Now.ToString("MM-dd-HH-mm-ss") + traceExtension;
                                }

                                Log.Write($"Collecting trace file '{traceDestination}' ...");

                                await jobConnection.DownloadDotnetTrace(traceDestination);
                            }
                            catch (Exception e)
                            {
                                Log.Write($"Error while fetching published assets for '{dependency}'");
                                Log.Verbose(e.Message);
                            }
                        }
                    }
                }

                // Stop all jobs in reverse dependency order (clients first)
                foreach (var dependency in Enumerable.Reverse(configuration.Dependencies))
                {
                    var service = configuration.Services[dependency];

                    var jobs = jobsByDependency[dependency];

                    await Task.WhenAll(jobs.Select(job => job.StopAsync()));
                }

                // Download assets
                foreach (var dependency in configuration.Dependencies)
                {
                    var service = configuration.Services[dependency];

                    var jobConnections = jobsByDependency[dependency];

                    foreach (var jobConnection in jobConnections)
                    {
                        // Fetch published folder
                        if (jobConnection.Job.Options.Fetch)
                        {
                            try
                            {
                                var fetchDestination = jobConnection.Job.Options.FetchOutput;

                                if (String.IsNullOrEmpty(fetchDestination) || !fetchDestination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    // If it does not end with a *.zip then we add a DATE.zip to it
                                    if (String.IsNullOrEmpty(fetchDestination))
                                    {
                                        fetchDestination = dependency;
                                    }

                                    fetchDestination = fetchDestination + "." + DateTime.Now.ToString("MM-dd-HH-mm-ss") + ".zip";
                                }

                                Log.Write($"Creating published assets '{fetchDestination}' ...");

                                await jobConnection.FetchAsync(fetchDestination);
                            }
                            catch (Exception e)
                            {
                                Log.Write($"Error while fetching published assets for '{dependency}'");
                                Log.Verbose(e.Message);
                            }
                        }

                        // Download individual files
                        if (jobConnection.Job.Options.DownloadFiles != null && jobConnection.Job.Options.DownloadFiles.Any())
                        {
                            foreach (var file in jobConnection.Job.Options.DownloadFiles)
                            {
                                Log.Write($"Downloading file '{file}' for '{dependency}'");

                                try
                                {
                                    await jobConnection.DownloadFileAsync(file);
                                }
                                catch (Exception e)
                                {
                                    Log.Write($"Error while downloading file {file}, skipping ...");
                                    Log.Verbose(e.Message);
                                    continue;
                                }
                            }
                        }
                    }
                }

                // Delete jobs
                foreach (var dependency in Enumerable.Reverse(configuration.Dependencies))
                {
                    var service = configuration.Services[dependency];

                    var jobs = jobsByDependency[dependency];

                    await Task.WhenAll(jobs.Select(job => job.DeleteAsync()));
                }

                // Display results
                foreach (var dependency in configuration.Dependencies)
                {
                    var service = configuration.Services[dependency];

                    var jobConnections = jobsByDependency[dependency];

                    Log.Quiet("");
                    Log.Quiet($"{dependency}");
                    Log.Quiet($"-------");

                    foreach (var jobConnection in jobConnections)
                    {
                        Log.Quiet("");
                        Log.Quiet($"SDK:                         {jobConnection.Job.SdkVersion}");
                        Log.Quiet($"Runtime:                     {jobConnection.Job.RuntimeVersion}");
                        Log.Quiet($"ASP.NET Core:                {jobConnection.Job.AspNetCoreVersion}");

                        WriteMeasures(jobConnection);

                        // Display output log
                        if (jobConnection.Job.Options.DisplayOutput)
                        {
                            Log.Quiet("");
                            Log.Quiet("Output:");
                            Log.Quiet("");
                            Log.DisplayOutput(jobConnection.Job.Output);
                        }


                        // Display build log
                        if (jobConnection.Job.Options.DisplayBuild)
                        {
                            try
                            {
                                Log.Quiet("");
                                Log.Quiet("Build:");
                                Log.Quiet("");

                                Log.DisplayOutput(await jobConnection.DownloadBuildLog());
                            }
                            catch (Exception e)
                            {
                                Log.Write($"Error while downloading build logs");
                                Log.Verbose(e.Message);
                            }
                        }
                    }
                }
            }

            return 0;
        }

        public static JObject MergeVariables(params JObject[] variableObjects)
        {
            var mergeOptions = new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge };

            var result = new JObject();

            foreach(var variableObject in variableObjects)
            {
                result.Merge(variableObject, mergeOptions);
            }

            return result;
        }

        public static Configuration LoadConfiguration(IEnumerable<string> configurationFileOrUrls, string profile, IEnumerable<KeyValuePair<string, string>> arguments)
        {
            JObject configuration = null;

            foreach (var configurationFileOrUrl in configurationFileOrUrls)
            {
                JObject localconfiguration;

                if (!string.IsNullOrWhiteSpace(configurationFileOrUrl))
                {
                    string configurationContent;

                    // Load the job definition from a url or locally
                    try
                    {
                        if (configurationFileOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            configurationContent = _httpClient.GetStringAsync(configurationFileOrUrl).GetAwaiter().GetResult();
                        }
                        else
                        {
                            configurationContent = File.ReadAllText(configurationFileOrUrl);
                        }
                    }
                    catch
                    {
                        throw new Exception($"Configuration '{configurationFileOrUrl}' could not be loaded.");
                    }

                    localconfiguration = JObject.Parse(configurationContent);
                }
                else
                {
                    throw new Exception($"Invalid file path or url: '{configurationFileOrUrl}'");
                }

                if (configuration != null)
                {
                    var mergeOptions = new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge };

                    configuration.Merge(localconfiguration);
                }
                else
                {
                    configuration = localconfiguration;
                }
            }

            var configurationInstance = configuration.ToObject<Configuration>();

            // Roundtrip the JObject such that it contains all the exta properties of the Configuration class that are not in the configuration file
            configuration = JObject.FromObject(configurationInstance);

            // Apply profile properties if a profile name is provided
            if (!String.IsNullOrWhiteSpace(profile))
            {
                if (!configurationInstance.Profiles.ContainsKey(profile))
                {
                    throw new Exception($"The profile `{profile}` was not found");
                }

                PatchObject(configuration, JObject.FromObject(configurationInstance.Profiles[profile]));
            }

            // Apply custom arguments
            foreach (var argument in Arguments)
            {
                JToken node = configuration;

                var segments = argument.Key.Split('.');

                foreach (var segment in segments)
                {
                    node = ((JObject)node).GetValue(segment, StringComparison.OrdinalIgnoreCase);
                }

                if (node is JArray jArray)
                {
                    jArray.Add(argument.Value);
                }
                else if (node is JValue jValue)
                {
                    // The value is automatically converted to the destination type
                    jValue.Value = argument.Value;
                }
            }

            var result = configuration.ToObject<Configuration>();

            // TODO: Post process the services to re-order them based on weight
            // result.Dependencies = result.Dependencies.OrderBy(x => x, x.Contains(":") ? int.Parse(x.Split(':', 2)[1]) : 100);

            // Override default values in ServerJob
            foreach (var dependency in result.Dependencies)
            {
                var service = result.Services[dependency];
                service.NoArguments = true;
            }

            return result;
        }

        public static void PatchObject(JObject source, JObject patch)
        {
            foreach(var patchProperty in patch)
            {
                var sourceProperty = source.Properties().Where(x => x.Name.Equals(patchProperty.Key, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

                // The property to patch exists
                if (sourceProperty != null)
                {
                    // if it's an object, patch it recursively
                    if (sourceProperty.Value.Type == JTokenType.Object)
                    {
                        PatchObject((JObject)sourceProperty.Value, (JObject)patchProperty.Value);
                    }
                    else if (sourceProperty.Value.Type == JTokenType.Array)
                    {
                        ((JArray)sourceProperty.Value).Add(patchProperty.Value.DeepClone());
                    }
                    else
                    {
                        sourceProperty.Value = patchProperty.Value;
                    }
                }
                else
                {
                    source.Add(patchProperty.Key, patchProperty.Value.DeepClone());
                }
            }
        }

        private static Func<IEnumerable<double>, double> Percentile(int percentile)
        {
            return list =>
            {
                var orderedList = list.OrderBy(x => x).ToArray();

                var nth = (int)Math.Ceiling((double)orderedList.Length * percentile / 100);

                return orderedList[nth];
            };
        }

        private static void WriteMeasures(JobConnection job)
        {
            // Handle old server versions that don't expose measurements
            if (!job.Job.Measurements.Any() || !job.Job.Metadata.Any())
            {
                return;
            }

            // Group by name for easy lookup
            var measurements = job.Job.Measurements.GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.ToList());
            var maxWidth = job.Job.Metadata.Max(x => x.ShortDescription.Length) + 2;

            var previousSource = "";

            foreach (var metadata in job.Job.Metadata)
            {
                if (!measurements.ContainsKey(metadata.Name))
                {
                    continue;
                }

                if (previousSource != metadata.Source)
                {
                    Log.Quiet("");
                    Log.Quiet($"## {metadata.Source}:");

                    previousSource = metadata.Source;
                }

                double result = 0;

                switch (metadata.Aggregate)
                {
                    case Operation.Avg:
                        result = measurements[metadata.Name].Average(x => Convert.ToDouble(x.Value));
                        break;

                    case Operation.Count:
                        result = measurements[metadata.Name].Count();
                        break;

                    case Operation.Max:
                        result = measurements[metadata.Name].Max(x => Convert.ToDouble(x.Value));
                        break;

                    case Operation.Median:
                        result = Percentile(50)(measurements[metadata.Name].Select(x => Convert.ToDouble(x.Value)));
                        break;

                    case Operation.Min:
                        result = measurements[metadata.Name].Min(x => Convert.ToDouble(x.Value));
                        break;

                    case Operation.Sum:
                        result = measurements[metadata.Name].Sum(x => Convert.ToDouble(x.Value));
                        break;
                }

                Console.WriteLine($"{(metadata.ShortDescription + ":").PadRight(maxWidth)} {result.ToString(metadata.Format)}");
            }
        }
    }
}
