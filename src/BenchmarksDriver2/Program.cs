// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Benchmarks.ClientJob;
using Benchmarks.ServerJob;
using BenchmarksDriver.Serializers;
using Fluid;
using Fluid.Values;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using YamlDotNet.Serialization;

namespace BenchmarksDriver
{
    public class Program
    {
        private static TimeSpan _timeout = TimeSpan.FromMinutes(5);

        private static readonly HttpClient _httpClient;
        private static readonly HttpClientHandler _httpClientHandler;

        private static string _tableName = "Benchmarks";
        private static string _sqlConnectionString = "";

        private const string EventPipeOutputFile = "eventpipe.netperf";

        // Default to arguments which should be sufficient for collecting trace of default Plaintext run
        private const string _defaultTraceArguments = "BufferSizeMB=1024;CircularMB=1024;clrEvents=JITSymbols;kernelEvents=process+thread+ImageLoad+Profile";

        private static CommandOption
            _outputArchiveOption,
            _buildArchiveOption,

            _configOption,
            _scenarioOption,
            _profileOption,
            _outputOption,
            _compareOption,
            _variableOption,
            _sqlConnectionStringOption,
            _sqlTableOption,
            _sessionOption,
            _descriptionOption,
            _propertyOption,
            _excludeMetadataOption,
            _excludeMeasurementsOption,
            _autoflush
            ;

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
                OptionsComparison = StringComparison.OrdinalIgnoreCase,                
            };

            app.HelpOption("-?|-h|--help");

            _configOption = app.Option("-c|--config", "Configuration file or url", CommandOptionType.MultipleValue);
            _scenarioOption = app.Option("-s|--scenario", "Scenario to execute", CommandOptionType.SingleValue);
            _profileOption = app.Option("--profile", "Profile name", CommandOptionType.MultipleValue);
            _outputOption = app.Option("-o|--output", "Output filename", CommandOptionType.SingleValue);
            _compareOption = app.Option("--compare", "An optional filename to compare the results to. Can be used multiple times.", CommandOptionType.MultipleValue);
            _variableOption = app.Option("-v|--variable", "Variable", CommandOptionType.MultipleValue);
            _sqlConnectionStringOption = app.Option("--sql",
                "Connection string of the SQL Server Database to store results in", CommandOptionType.SingleValue);
            _sqlTableOption = app.Option("--table",
                "Table name of the SQL Database to store results in", CommandOptionType.SingleValue);
            _sessionOption = app.Option("--session", "A logical identifier to group related jobs.", CommandOptionType.SingleValue);
            _descriptionOption = app.Option("--description", "A string describing the job.", CommandOptionType.SingleValue);
            _propertyOption = app.Option("-p|--property", "Some custom key/value that will be added to the results, .e.g. --property arch=arm --property os=linux", CommandOptionType.MultipleValue);
            _excludeMeasurementsOption = app.Option("--no-measurements", "Remove all measurements from the stored results. For instance, all samples of a measure won't be stored, only the final value.", CommandOptionType.SingleOrNoValue);
            _excludeMetadataOption = app.Option("--no-metadata", "Remove all metadata from the stored results. The metadata is only necessary for being to generate friendly outputs.", CommandOptionType.SingleOrNoValue);
            _autoflush = app.Option("--auto-flush", "Runs a single long-running job and flushes measurements automatically.", CommandOptionType.NoValue);

            // Extract dynamic arguments
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                if (arg.StartsWith("--") && !app.Options.Any(option => arg.StartsWith("--" + option.LongName)))
                {
                    // Remove this argument from the command line
                    args[i] = "";

                    // Dynamic arguments always come in pairs 
                    if (i + 1 < args.Length)
                    {
                        Arguments.Add(KeyValuePair.Create(arg.Substring(2), args[i + 1]));
                        args[i + 1] = "";

                        i++;
                    }
                }
            }

            // Driver Options
            
            var verboseOption = app.Option("-v|--verbose",
                "Verbose output", CommandOptionType.NoValue);
            var quietOption = app.Option("--quiet",
                "Quiet output, only the results are displayed", CommandOptionType.NoValue);
            var iterationsOption = app.Option("-i|--iterations",
                "The number of iterations.", CommandOptionType.SingleValue);
            var excludeOption = app.Option("-x|--exclude",
                "The number of best and worst jobs to skip.", CommandOptionType.SingleValue);
            var shutdownOption = app.Option("--before-shutdown",
                "An endpoint to call before the application has shut down.", CommandOptionType.SingleValue);
            var spanOption = app.Option("-sp|--span",
                "The time during which the client jobs are repeated, in 'HH:mm:ss' format. e.g., 48:00:00 for 2 days.", CommandOptionType.SingleValue);
            var benchmarkdotnetOption = app.Option("--benchmarkdotnet",
                "Runs a BenchmarkDotNet application, with an optional filter. e.g., --benchmarkdotnet, --benchmarkdotnet:*MyBenchmark*", CommandOptionType.SingleOrNoValue);

            // ServerJob Options
            var databaseOption = app.Option("--database",
                "The type of database to run the benchmarks with (PostgreSql, SqlServer or MySql). Default is None.", CommandOptionType.SingleValue);
            var kestrelThreadCountOption = app.Option("--kestrelThreadCount",
                "Maps to KestrelServerOptions.ThreadCount.",
                CommandOptionType.SingleValue);
            
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

            app.OnExecute(async () =>
            {
                Log.IsQuiet = quietOption.HasValue();
                Log.IsVerbose = verboseOption.HasValue();

                if (serverTimeoutOption.HasValue())
                {
                    TimeSpan.TryParse(serverTimeoutOption.Value(), out _timeout);
                }

                var session = _sessionOption.Value();
                if (string.IsNullOrEmpty(session))
                {
                    session = Guid.NewGuid().ToString("n");
                }

                var description = _descriptionOption.Value() ?? "";

                if (iterationsOption.HasValue() && spanOption.HasValue())
                {
                    Console.WriteLine($"The options --iterations and --span can't be used together.");

                    app.ShowHelp();
                    return 10;
                }

                var iterations = 1;
                var exclude = 0;

                var span = TimeSpan.Zero;

                if (_sqlTableOption.HasValue())
                {
                    _tableName = _sqlTableOption.Value();

                    if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable(_tableName)))
                    {
                        _tableName = Environment.GetEnvironmentVariable(_tableName);
                    }
                }

                if (_sqlConnectionStringOption.HasValue())
                {
                    _sqlConnectionString = _sqlConnectionStringOption.Value();

                    if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable(_sqlConnectionString)))
                    {
                        _sqlConnectionString = Environment.GetEnvironmentVariable(_sqlConnectionString);
                    }
                }

                if (!_scenarioOption.HasValue() && !_compareOption.HasValue())
                {
                    Console.Error.WriteLine("Scenario name must be specified.");
                    return 1;
                }                

                var results = new ExecutionResult();

                var scenarioName = _scenarioOption.Value();

                if (scenarioName != null)
                {
                    var variables = new JObject();

                    foreach (var variable in _variableOption.Values)
                    {
                        var segments = variable.Split('=', 2);

                        if (segments.Length != 2)
                        {
                            Console.WriteLine($"Invalid variable argument: '{variable}', format is \"[NAME]=[VALUE]\"");

                            app.ShowHelp();
                            return -1;
                        }

                        variables[segments[0]] = segments[1];
                    }

                    foreach (var property in _propertyOption.Values)
                    {
                        var segments = property.Split('=', 2);

                        if (segments.Length != 2)
                        {
                            Console.WriteLine($"Invalid property argument: '{property}', format is \"[NAME]=[VALUE]\"");

                            app.ShowHelp();
                            return -1;
                        }
                    }

                    var configuration = await BuildConfigurationAsync(_configOption.Values, scenarioName, Arguments, variables, _profileOption.Values);

                    var serializer = new Serializer();

                    // Storing the list of services to run as part of the selected scenario
                    var dependencies = configuration.Scenarios[scenarioName].Select(x => x.Key).ToArray();

                    // Verifying endpoints
                    foreach (var jobName in dependencies)
                    {
                        var service = configuration.Jobs[jobName];

                        foreach (var endpoint in service.Endpoints)
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
                    
                    // Initialize database
                    if (!String.IsNullOrWhiteSpace(_sqlConnectionString))
                    {
                        await JobSerializer.InitializeDatabaseAsync(_sqlConnectionString, _tableName);
                    }

                    Log.Write($"Running session '{session}' with description '{_descriptionOption.Value()}'");

                    if (_autoflush.HasValue())
                    {
                        results = await RunAutoFlush(
                            configuration,
                            scenarioName,
                            session,
                            span
                            );
                    }
                    else
                    {
                        results = await Run(
                            configuration,
                            scenarioName,
                            session,
                            iterations,
                            exclude,
                            shutdownOption.Value(),
                            span
                            );
                    }
                }

                // Display diff

                if (_compareOption.HasValue())
                {
                    foreach (var filename in _compareOption.Values)
                    {
                        if (!File.Exists(filename))
                        {
                            Log.Write($"Diff source file not found: '{new FileInfo(filename).FullName}'", notime: true);
                            return -1;
                        }
                    }

                    var compareResults = _compareOption.Values.Select(filename => JsonConvert.DeserializeObject<JobResults>(File.ReadAllText(filename))).ToList();

                    compareResults.Add(results.JobResults);

                    var resultNames = _compareOption.Values.Select(filename => Path.GetFileNameWithoutExtension(filename)).ToList();

                    if (_scenarioOption.HasValue())
                    {
                        if (_outputOption.HasValue())
                        {
                            resultNames.Add(Path.GetFileNameWithoutExtension(_outputOption.Value()));
                        }
                        else
                        {
                            resultNames.Add("Current");
                        }
                    }

                    DisplayDiff(compareResults, resultNames);
                }

                return results.ReturnCode;
            });

            try
            {
                return app.Execute(args.Where(x => !String.IsNullOrEmpty(x)).ToArray());
            }
            catch (CommandParsingException e)
            {
                Console.WriteLine(e.ToString());
                return -1;
            }
        }

        private static async Task<ExecutionResult> Run(
            Configuration configuration,
            string scenarioName,
            string session,
            int iterations,
            int exclude,
            string shutdownEndpoint,
            TimeSpan span
            )
        {
            // Storing the list of services to run as part of the selected scenario
            var dependencies = configuration.Scenarios[scenarioName].Select(x => x.Key).ToArray();

            var executionResults = new ExecutionResult();

            for (var i = 1; i <= iterations; i++)
            {
                if (iterations > 1)
                {
                    Log.Write($"Job {i} of {iterations}");
                }

                var jobsByDependency = new Dictionary<string, List<JobConnection>>();

                foreach (var jobName in dependencies)
                {
                    var service = configuration.Jobs[jobName];
                    service.DriverVersion = 2;

                    var jobs = service.Endpoints.Select(endpoint => new JobConnection(service, new Uri(endpoint))).ToList();

                    jobsByDependency[jobName] = jobs;

                    // Check that each configured agent endpoint for this service 
                    // has a compatible OS
                    if (!String.IsNullOrEmpty(service.Options.RequiredOperatingSystem))
                    {
                        foreach (var job in jobs)
                        {
                            var info = await job.GetInfoAsync();

                            var os = info["os"]?.ToString();

                            if (!String.Equals(os, service.Options.RequiredOperatingSystem, StringComparison.OrdinalIgnoreCase))
                            {
                                Log.Write($"Scenario skipped as the agent doesn't match the OS constraint ({service.Options.RequiredOperatingSystem}) on service '{jobName}'");
                                return new ExecutionResult();
                            }
                        }
                    }

                    // Start this service on all configured agent endpoints
                    await Task.WhenAll(
                        jobs.Select(job =>
                        {
                            // Start job on agent
                            return job.StartAsync(
                                jobName,
                                _outputArchiveOption,
                                _buildArchiveOption
                            );
                        })
                    );

                    // Start threads that will keep the jobs alive
                    foreach (var job in jobs)
                    {
                        job.StartKeepAlive();
                    }

                    if (service.WaitForExit)
                    {
                        // Wait for all clients to stop
                        while (true)
                        {
                            var stop = true;

                            foreach (var job in jobs)
                            {
                                var state = await job.GetStateAsync();

                                stop = stop && (state == ServerState.Stopped || state == ServerState.Failed);
                            }

                            if (stop)
                            {
                                break;
                            }
                            
                            await Task.Delay(1000);
                        }
                        
                        // Stop a blocking job
                        await Task.WhenAll(jobs.Select(job => job.StopAsync()));

                        await Task.WhenAll(jobs.Select(job => job.TryUpdateJobAsync()));

                        await Task.WhenAll(jobs.Select(job => job.DownloadAssetsAsync(jobName)));

                        await Task.WhenAll(jobs.Select(job => job.DeleteAsync()));
                    }
                }

                // Download traces, before the jobs are stopped
                foreach (var jobName in dependencies)
                {
                    var service = configuration.Jobs[jobName];

                    var jobConnections = jobsByDependency[jobName];

                    foreach (var jobConnection in jobConnections)
                    {
                        var info = await jobConnection.GetInfoAsync();
                        var os = Enum.Parse<Benchmarks.ServerJob.OperatingSystem>(info["os"]?.ToString() ?? "linux", ignoreCase: true);

                        var traceExtension = ".nettrace";

                        // Download trace
                        if (jobConnection.Job.DotNetTrace || jobConnection.Job.Collect)
                        {
                            if (jobConnection.Job.Collect)
                            {
                                traceExtension = os == Benchmarks.ServerJob.OperatingSystem.Windows
                                    ? ".etl.zip"
                                    : ".trace.zip"
                                    ;
                            }

                            try
                            {
                                var traceDestination = jobConnection.Job.Options.TraceOutput;

                                if (String.IsNullOrWhiteSpace(traceDestination))
                                {
                                    traceDestination = jobName;
                                }

                                
                                if (!traceDestination.EndsWith(traceExtension, StringComparison.OrdinalIgnoreCase))
                                {
                                    traceDestination = traceDestination + "." + DateTime.Now.ToString("MM-dd-HH-mm-ss") + traceExtension;
                                }

                                Log.Write($"Collecting trace file '{traceDestination}' ...");

                                await jobConnection.DownloadDotnetTrace(traceDestination);
                            }
                            catch (Exception e)
                            {
                                Log.Write($"Error while fetching trace for '{jobName}'");
                                Log.Verbose(e.Message);
                            }
                        }
                    }
                }

                // Stop all non-blocking jobs in reverse dependency order (clients first)
                foreach (var jobName in dependencies)
                {
                    var service = configuration.Jobs[jobName];

                    if (!service.WaitForExit)
                    {
                        var jobs = jobsByDependency[jobName];

                        await Task.WhenAll(jobs.Select(job => job.StopAsync()));

                        await Task.WhenAll(jobs.Select(job => job.TryUpdateJobAsync()));

                        await Task.WhenAll(jobs.Select(job => job.DownloadAssetsAsync(jobName)));

                        await Task.WhenAll(jobs.Select(job => job.DeleteAsync()));
                    }
                }

                // Display results
                foreach (var jobName in dependencies)
                {
                    var service = configuration.Jobs[jobName];

                    var jobConnections = jobsByDependency[jobName];

                    if (!service.Options.DiscardResults)
                    {
                        Log.Quiet("");
                        Log.Quiet($"{jobName}");
                        Log.Quiet($"-------");
                    }

                    foreach (var jobConnection in jobConnections)
                    {
                        // Convert any json result to an object
                        NormalizeResults(jobConnections);

                        if (!service.Options.DiscardResults)
                        {
                            WriteMeasures(jobConnection);
                        }
                    }
                }

                var jobResults = await CreateJobResultsAsync(configuration, dependencies, jobsByDependency);

                foreach (var property in _propertyOption.Values)
                {
                    var segments = property.Split('=', 2);

                    jobResults.Properties[segments[0]] = segments[1];
                }

                executionResults.JobResults = jobResults;
            }

            // Save results

            if (_outputOption.HasValue())
            {
                var filename = _outputOption.Value();

                if (File.Exists(filename))
                {
                    File.Delete(filename);
                }

                await File.WriteAllTextAsync(filename, JsonConvert.SerializeObject(executionResults.JobResults, Formatting.Indented, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }));

                Log.Write("", notime: true);
                Log.Write($"Results saved in '{new FileInfo(filename).FullName}'", notime: true);
            }

            // Store data

            if (!String.IsNullOrEmpty(_sqlConnectionString))
            {
                await JobSerializer.WriteJobResultsToSqlAsync(executionResults.JobResults, _sqlConnectionString, _tableName, session, _scenarioOption.Value(), _descriptionOption.Value());
            }

            return executionResults;
        }

        private static async Task<ExecutionResult> RunAutoFlush(
            Configuration configuration,
            string scenarioName,
            string session,
            TimeSpan span
            )
        {
            var executionResults = new ExecutionResult();

            // Storing the list of services to run as part of the selected scenario
            var dependencies = configuration.Scenarios[scenarioName].Select(x => x.Key).ToArray();

            if (dependencies.Length != 1)
            {
                Log.Write($"With --auto-flush a single job is required.");
                return executionResults;
            }

            var jobName = dependencies.First();
            var service = configuration.Jobs[jobName];
            
            if (service.Endpoints.Count() != 1)
            {
                Log.Write($"With --auto-flush a single endpoint is required.");
                return executionResults;
            }

            if (!service.WaitForExit && span == TimeSpan.Zero)
            {
                Log.Write($"With --auto-flush a --span duration or a blocking job is required (missing 'waitForExit' option).");
                return executionResults;
            }

            service.DriverVersion = 2;

            var job = new JobConnection(service, new Uri(service.Endpoints.First()));

            // Check that each configured agent endpoint for this service 
            // has a compatible OS
            if (!String.IsNullOrEmpty(service.Options.RequiredOperatingSystem))
            {
                var info = await job.GetInfoAsync();

                var os = info["os"]?.ToString();

                if (!String.Equals(os, service.Options.RequiredOperatingSystem, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Write($"Scenario skipped as the agent doesn't match the OS constraint ({service.Options.RequiredOperatingSystem}) on service '{jobName}'");
                    return new ExecutionResult();
                }
            }

            // Start this service on the configured agent endpoint
            await job.StartAsync(jobName, _outputArchiveOption, _buildArchiveOption);

            // Start threads that will keep the jobs alive
            job.StartKeepAlive();

            var start = DateTime.UtcNow;

            // Wait for the job to stop
            while (true)
            {
                await Task.Delay(5000);

                await job.TryUpdateJobAsync();

                var stop = job.Job.State == ServerState.Stopped || job.Job.State == ServerState.Deleted || job.Job.State == ServerState.Failed;

                if (start + span > DateTime.UtcNow)
                {
                    stop = true;
                }

                if (job.Job.Measurements.Any(x => x.IsDelimiter))
                {
                    // Remove all values after the delimiter locally
                    Measurement measurement;
                    var measurements = new List<Measurement>();

                    do
                    {
                        job.Job.Measurements.TryDequeue(out measurement);
                        measurements.Add(measurement);
                    } while (!measurement.IsDelimiter);

                    job.Job.Measurements = new ConcurrentQueue<Measurement>(measurements);

                    // Removes all values before the delimiter on the server
                    await job.FlushMeasurements();

                    // Convert any json result to an object
                    NormalizeResults(new[] { job });

                    if (!service.Options.DiscardResults)
                    {
                        WriteMeasures(job);
                    }

                    var jobResults = await CreateJobResultsAsync(configuration, dependencies, new Dictionary<string, List<JobConnection>> { [jobName] = new List<JobConnection> { job } });

                    foreach (var property in _propertyOption.Values)
                    {
                        var segments = property.Split('=', 2);

                        jobResults.Properties[segments[0]] = segments[1];
                    }

                    // Save results

                    if (_outputOption.HasValue())
                    {
                        var filename = _outputOption.Value();
                        var index = 1;

                        do
                        {
                            var filenameWithoutExtension = Path.GetFileNameWithoutExtension(_outputOption.Value());
                            filename = filenameWithoutExtension + "-" + index++ + Path.GetExtension(_outputOption.Value());
                        } while (File.Exists(filename));

                        await File.WriteAllTextAsync(filename, JsonConvert.SerializeObject(jobResults, Formatting.Indented, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }));

                        Log.Write("", notime: true);
                        Log.Write($"Results saved in '{new FileInfo(filename).FullName}'", notime: true);
                    }

                    // Store data

                    if (!String.IsNullOrEmpty(_sqlConnectionString))
                    {
                        await JobSerializer.WriteJobResultsToSqlAsync(jobResults, _sqlConnectionString, _tableName, session, _scenarioOption.Value(), _descriptionOption.Value());
                    }
                }

                if (stop)
                {
                    break;
                }
            }

            await job.StopAsync();

            await  job.TryUpdateJobAsync();

            await  job.DownloadAssetsAsync(jobName);

            await  job.DeleteAsync();

            return executionResults;
        }

        private static void DisplayDiff(IEnumerable<JobResults> allResults, IEnumerable<string> allNames)
        {
            // Use the first job results as the reference for metadata:
            var firstJob = allResults.First();

            foreach(var jobEntry in firstJob.Jobs)
            {
                var jobName = jobEntry.Key;
                var jobResult = jobEntry.Value;

                Console.WriteLine();

                var table = new ResultTable(allNames.Count() * 2 + 1 - 1); // two columns per job, minus the first job, plus the description

                table.Headers.Add(jobName);

                foreach(var name in allNames)
                {
                    table.Headers.Add(name);

                    if (name != allNames.First())
                    {
                        table.Headers.Add(""); // percentage
                    }
                }

                foreach (var metadata in jobResult.Metadata)
                {
                    if (!jobResult.Results.ContainsKey(metadata.Name))
                    {
                        continue;
                    }

                    // We don't render the result if it's a raw object

                    if (metadata.Format == "object")
                    {
                        continue;
                    }

                    var row = table.AddRow();

                    var cell = new Cell();

                    cell.Elements.Add(new CellElement() { Text = metadata.ShortDescription, Alignment = CellTextAlignment.Left });

                    row.Add(cell);

                    foreach (var result in allResults)
                    {
                        // Skip jobs that have no data for this measure
                        if (!result.Jobs.ContainsKey(jobName))
                        {
                            row.Add(new Cell());
                            row.Add(new Cell());

                            continue;
                        }

                        var job = result.Jobs[jobName];

                        if (!String.IsNullOrEmpty(metadata.Format))
                        {
                            var measure = Convert.ToDouble(job.Results.ContainsKey(metadata.Name) ? job.Results[metadata.Name] : 0);
                            var previous = Convert.ToDouble(jobResult.Results.ContainsKey(metadata.Name) ? jobResult.Results[metadata.Name] : 0);

                            var improvement = measure == 0
                            ? 0
                            : (measure - previous) / previous * 100;

                            row.Add(cell = new Cell());

                            cell.Elements.Add(new CellElement { Text = Convert.ToDouble(measure).ToString(metadata.Format), Alignment = CellTextAlignment.Right });

                            // Don't render % on baseline job
                            if (firstJob != result)
                            {
                                row.Add(cell = new Cell());

                                if (measure != 0)
                                {
                                    var sign = improvement > 0 ? "+" : "";
                                    cell.Elements.Add(new CellElement { Text = $"{sign}{improvement:n2}%", Alignment = CellTextAlignment.Right });
                                }
                            }
                        }
                        else
                        {
                            var measure = job.Results.ContainsKey(metadata.Name) ? job.Results[metadata.Name] : 0;

                            row.Add(cell = new Cell());
                            cell.Elements.Add(new CellElement { Text = measure.ToString(), Alignment = CellTextAlignment.Right });

                            // Don't render % on baseline job
                            if (firstJob != result)
                            {
                                row.Add(new Cell());
                            }

                        }
                    }
                }

                table.Render(Console.Out);

                Console.WriteLine();
            }
        }

        public static JObject MergeVariables(params object[] variableObjects)
        {
            var mergeOptions = new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge };

            var result = new JObject();

            foreach(var variableObject in variableObjects)
            {
                if (variableObject == null)
                {
                    continue;
                }

                result.Merge(JObject.FromObject(variableObject), mergeOptions);
            }

            return result;
        }

        /// <summary>
        /// Applies all command line argument to alter the configuration files and build a final Configuration instance.
        /// 1- Merges the configuration files in the same order as requested
        /// 2- For each scenario's job, clone it in the Configuration's jobs list
        /// 3- Path the new job with the scenario's properties
        /// </summary>
        public static async Task<Configuration> BuildConfigurationAsync(
            IEnumerable<string> configurationFileOrUrls, 
            string scenarioName, 
            IEnumerable<KeyValuePair<string, string>> arguments, 
            JObject commandLineVariables,
            IEnumerable<string> profiles
            )
        {
            JObject configuration = null;

            // Merge all configuration sources
            foreach (var configurationFileOrUrl in configurationFileOrUrls)
            {
                var localconfiguration = await LoadConfigurationAsync(configurationFileOrUrl);

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

            // Roundtrip the JObject such that it contains all the exta properties of the Configuration class that are not in the configuration file
            var configurationInstance = configuration.ToObject<Configuration>();

            // After that point we only modify the concrete instance of Configuration
            if (!configurationInstance.Scenarios.ContainsKey(scenarioName))
            {
                throw new Exception($"The scenario `{scenarioName}` was not found");
            }
            
            var scenario = configurationInstance.Scenarios[scenarioName];

            // Clone each service from the selected scenario inside the Jobs property of the Configuration
            foreach (var service in scenario)
            {
                var jobName = service.Value.Job;
                var serviceName = service.Key;

                if (!configurationInstance.Jobs.ContainsKey(jobName))
                {
                    throw new Exception($"The job named `{jobName}` was not found for `{serviceName}`");
                }

                var jobObject = JObject.FromObject(configurationInstance.Jobs[jobName]);
                var dependencyObject = (JObject) configuration["scenarios"][scenarioName][serviceName];

                PatchObject(jobObject, dependencyObject);

                configurationInstance.Jobs[serviceName] = jobObject.ToObject<ServerJob>();
            }

            // Force all jobs as self-contained by default. This can be overrided by command line config.
            // This can't be done in ServerJob for backward compatibility
            foreach (var job in configurationInstance.Jobs)
            {
                job.Value.SelfContained = true;
            }

            // After that point we only modify the JObject representation of Configuration
            configuration = JObject.FromObject(configurationInstance);

            // Apply profiles
            foreach (var profileName in profiles)
            {
                if (!configurationInstance.Profiles.ContainsKey(profileName))
                {
                    throw new Exception($"Could not find a profile named '{profileName}'");
                }

                var mergeOptions = new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge };

                var profile = (JObject)configuration["Profiles"][profileName];

                // Fix casing
                if (profile["variables"] != null)
                {
                    profile[nameof(Configuration.Variables)] = profile["variables"];
                    profile.Remove("variables");
                }

                if (profile["jobs"] != null)
                {
                    profile[nameof(Configuration.Jobs)] = profile["jobs"];
                    profile.Remove("jobs");
                }

                configuration.Merge(profile, mergeOptions);
            }

            // Apply custom arguments
            foreach (var argument in arguments)
            {
                JToken node = configuration["Jobs"];

                var segments = argument.Key.Split('.');

                foreach (var segment in segments)
                {
                    node = ((JObject)node).GetValue(segment, StringComparison.OrdinalIgnoreCase);

                    if (node == null)
                    {
                        throw new Exception($"Could not find part of the configuration path: '{argument}'");
                    }
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
                else if (node is JObject jObject)
                {
                    // String to Object mapping -> try to parse as KEY=VALUE
                    var argumentSegments = argument.Value.ToString().Split('=', 2);

                    if (argumentSegments.Length != 2)
                    {
                        throw new Exception($"Argument value '{argument.Value}' could not assigned to `{segments.Last()}`.");
                    }

                    jObject[argumentSegments[0]] = argumentSegments[1];
                }
            }

            // Evaluate templates

            foreach (JProperty property in configuration["Jobs"] ?? new JObject())
            {
                var job = property.Value;
                var rootVariables = configuration["Variables"] as JObject ?? new JObject();
                var jobVariables = job["Variables"] as JObject ?? new JObject();

                var variables = MergeVariables(rootVariables, jobVariables, commandLineVariables);

                ApplyTemplates(job, new TemplateContext { Model = variables });
            }

            var result = configuration.ToObject<Configuration>();

            // Override default values in ServerJob for backward compatibility as the server would automatically add custom arguments to the applications.
            foreach (var job in result.Jobs.Values)
            {
                job.NoArguments = true;
                job.Scenario = scenarioName;
            }

            return result;
        }

        private static void ApplyTemplates(JToken node, TemplateContext templateContext)
        {
            foreach(var token in node.Children())
            {
                if (token is JValue jValue)
                {
                    if (jValue.Type == JTokenType.String)
                    {
                        var template = jValue.ToString();

                        if (template.Contains("{"))
                        {
                            if (FluidTemplate.TryParse(template, out var tree))
                            {
                                jValue.Value = tree.Render(templateContext);
                            }
                        }
                    }
                }
                else
                {
                    ApplyTemplates(token, templateContext);
                }
            }
        }

        public static async Task<JObject> LoadConfigurationAsync(string configurationFilenameOrUrl)
        {
            JObject localconfiguration;

            if (!string.IsNullOrWhiteSpace(configurationFilenameOrUrl))
            {
                string configurationContent;

                // Load the job definition from a url or locally
                try
                {
                    if (configurationFilenameOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        configurationContent = await _httpClient.GetStringAsync(configurationFilenameOrUrl);
                    }
                    else
                    {
                        configurationContent = File.ReadAllText(configurationFilenameOrUrl);
                    }
                }
                catch
                {
                    throw new Exception($"Configuration '{configurationFilenameOrUrl}' could not be loaded.");
                }

                localconfiguration = null;

                switch (Path.GetExtension(configurationFilenameOrUrl))
                {
                    case ".json":
                        localconfiguration = JObject.Parse(configurationContent);
                        break;

                    case ".yml":
                    case ".yaml":

                        var deserializer = new DeserializerBuilder().Build();
                        var yamlObject = deserializer.Deserialize(new StringReader(configurationContent));

                        var serializer = new SerializerBuilder()
                            .JsonCompatible()
                            .Build();

                        var json = serializer.Serialize(yamlObject);
                        localconfiguration = JObject.Parse(json);
                        break;
                }
                
                // Process imports
                if (localconfiguration.ContainsKey("imports"))
                {
                    var mergeOptions = new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge };

                    foreach (JValue import in (JArray) localconfiguration.GetValue("imports"))
                    {
                        var importFilenameOrUrl = import.ToString();

                        var importedConfiguration = await LoadConfigurationAsync(importFilenameOrUrl);

                        if (importedConfiguration != null)
                        {
                            localconfiguration.Merge(importedConfiguration, mergeOptions);
                        }
                    }
                }

                localconfiguration.Remove("imports");

                return localconfiguration;
            }
            else
            {
                throw new Exception($"Invalid file path or url: '{configurationFilenameOrUrl}'");
            }            
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
                        if (patchProperty.Value.Type == JTokenType.Object)
                        {
                            // JObject to JObject mapping
                            PatchObject((JObject)sourceProperty.Value, (JObject)patchProperty.Value);
                        }
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

        private static void NormalizeResults(IEnumerable<JobConnection> jobs)
        {
            if (jobs == null || !jobs.Any())
            {
                return;
            }

            // For each job, compute the operation on each measurement
            foreach(var job in jobs)
            {
                // Group by name for easy lookup
                var measurements = job.Job.Measurements.GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.ToList());

                foreach (var metadata in job.Job.Metadata)
                {
                    if (!measurements.ContainsKey(metadata.Name))
                    {
                        continue;
                    }

                    if (metadata.Format == "json")
                    {
                        foreach (var measurement in measurements[metadata.Name])
                        {
                            measurement.Value = JsonConvert.DeserializeObject(measurement.Value.ToString());
                        }

                        metadata.Format = "object";
                    }
                }
            }
        }

        private static async Task<JobResults> CreateJobResultsAsync(Configuration configuration, string[] dependencies, Dictionary<string, List<JobConnection>> jobsByDependency)
        {
            var jobResults = new JobResults();

            foreach (var jobName in dependencies)
            {
                if (configuration.Jobs[jobName].Options.DiscardResults)
                {
                    continue;
                }

                var jobResult = jobResults.Jobs[jobName] = new JobResult();
                var jobConnections = jobsByDependency[jobName];

                jobResult.Results = SummarizeResults(jobConnections);

                // Insert metadata
                if (!_excludeMetadataOption.HasValue())
                {
                    jobResult.Metadata = jobConnections[0].Job.Metadata.ToArray();

                }

                // Insert measurements
                if (!_excludeMeasurementsOption.HasValue())
                {
                    foreach (var jobConnection in jobConnections)
                    {
                        jobResult.Measurements.Add(jobConnection.Job.Measurements.ToArray());
                    }
                }

                jobResult.Environment = await jobConnections.First().GetInfoAsync();
            }

            return jobResults;
        }

        private static Dictionary<string, object> SummarizeResults(IEnumerable<JobConnection> jobs)
        {
            if (jobs == null || !jobs.Any())
            {
                return new Dictionary<string, object>();
            }

            // For each job, compute the operation on each measurement
            var groups = jobs.Select(job =>
            {
                // Group by name for easy lookup
                var measurements = job.Job.Measurements.GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.ToList());

                var summaries = new Dictionary<string, object>();

                foreach (var metadata in job.Job.Metadata)
                {
                    if (!measurements.ContainsKey(metadata.Name))
                    {
                        continue;
                    }

                    object result = 0;

                    switch (metadata.Aggregate)
                    {
                        case Operation.All:
                            result = measurements[metadata.Name].Select(x => x.Value).ToArray();
                            break;

                        case Operation.First:
                            result = measurements[metadata.Name].First().Value;
                            break;

                        case Operation.Last:
                            result = measurements[metadata.Name].Last().Value;
                            break;

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

                        case Operation.Delta:
                            result = measurements[metadata.Name].Max(x => Convert.ToDouble(x.Value)) - measurements[metadata.Name].Min(x => Convert.ToDouble(x.Value));
                            break;

                        default:
                            result = measurements[metadata.Name].First().Value;
                            break;
                    }

                    if (!String.IsNullOrEmpty(metadata.Format) && metadata.Format != "object")
                    {
                        summaries[metadata.Name] = Convert.ToDouble(result);
                    }
                    else
                    {
                        summaries[metadata.Name] = result;
                    }
                }

                return summaries;
            }).ToArray();

            // Single job, no reduce operation is necessary
            if (groups.Length == 1)
            {
                return groups[0];
            }

            var reduced = new Dictionary<string, object>();

            foreach (var metadata in jobs.First().Job.Metadata)
            {
                var reducedValues = groups.SelectMany(x => x)
                    .Where(x => x.Key == metadata.Name);

                object reducedValue = null;

                switch (metadata.Aggregate)
                {
                    case Operation.All:
                        reducedValue = reducedValues.ToArray();
                        break;

                    case Operation.First:
                        reducedValue = reducedValues.First().Value;
                        break;

                    case Operation.Last:
                        reducedValue = reducedValues.Last().Value;
                        break;

                    case Operation.Avg:
                        reducedValue = reducedValues.Average(x => Convert.ToDouble(x.Value));
                        break;

                    case Operation.Count:
                        reducedValue = reducedValues.Count();
                        break;

                    case Operation.Max:
                        reducedValue = reducedValues.Max(x => Convert.ToDouble(x.Value));
                        break;

                    case Operation.Median:
                        reducedValue = Percentile(50)(reducedValues.Select(x => Convert.ToDouble(x.Value)));
                        break;

                    case Operation.Min:
                        reducedValue = reducedValues.Min(x => Convert.ToDouble(x.Value));
                        break;

                    case Operation.Sum:
                        reducedValue = reducedValues.Sum(x => Convert.ToDouble(x.Value));
                        break;

                    case Operation.Delta:
                        reducedValue = reducedValues.Max(x => Convert.ToDouble(x.Value)) - reducedValues.Min(x => Convert.ToDouble(x.Value));
                        break;

                    default:
                        reducedValue = reducedValues.First().Value;
                        break;
                }

                reduced[metadata.Name] = reducedValue;
            }

            return reduced;

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

                object result = 0;

                switch (metadata.Aggregate)
                {
                    case Operation.All:
                        result = measurements[metadata.Name].Select(x => x.Value).ToArray();
                        break;

                    case Operation.First:
                        result = measurements[metadata.Name].First().Value;
                        break;

                    case Operation.Last:
                        result = measurements[metadata.Name].Last().Value;
                        break;

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

                    case Operation.Delta:
                        result = measurements[metadata.Name].Max(x => Convert.ToDouble(x.Value)) - measurements[metadata.Name].Min(x => Convert.ToDouble(x.Value));
                        break;

                    default:
                        result = measurements[metadata.Name].First().Value;
                        break;
                }

                // We don't render the result if it's a raw object
                if (metadata.Format != "object")
                {
                    if (!String.IsNullOrEmpty(metadata.Format))
                    {
                        Console.WriteLine($"{(metadata.ShortDescription + ":").PadRight(maxWidth)} {Convert.ToDouble(result).ToString(metadata.Format)}");
                    }
                    else
                    {
                        Console.WriteLine($"{(metadata.ShortDescription + ":").PadRight(maxWidth)} {result.ToString()}");
                    }
                }
            }
        }
    }
}
