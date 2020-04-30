# ASP.NET Benchmark Driver

## Usage

```
Usage: BenchmarksDriver [options]

Options:
  -?|-h|--help                                          Show help information

  These options are not specific to a Job

  --config <filename>                                            Configuration file or url. Can be used multiple times.
  --scenario <scenario>                                          Scenario to execute.
  --profile <profile>                                            Profiles to apply. Can be used multiple times.
  --variable <key=value>                                         A global variable. can be used multiple times.
  --compare <filename>                                           An optional filename to compare the results to. Can be used multiple times.
  
  --session                                                      A logical identifier to group related jobs.
  --description                                                  A string describing the job.
  --property <key=value>                                         Some custom key/value that will be added to the results, .e.g. --property arch=arm --property os=linux
  
  --sql                                                          Connection string or environment variable name of the SQL Server Database to store results in.
  --table                                                        Table or environment variable name of the SQL Database to store results in.
  --output <filename>                                            An optional filename to store the output document.
  --no-metadata                                                  Don't record metadata in the stored document.
  --no-measurements                                              Don't record measurements in the stored document.

  --repeat <job>                                                 The job to repeat using the '--duration' argument.
  --duration <HH:mm:ss>                                          The duration while the job is repeated.
  --auto-flush                                                   Runs a single long-running job and flushes measurements automatically.

  These options are specific to a Job service named [JOB]

  --[JOB].endpoints <url>                                                  An endpoint on which to deploy the job definition, e.g., http://asp-perf-lin:5001. Can be used multiple times.

  ## Sources location

  --[JOB].source.repository                                                The git repository containing the source code to build, e.g., https://github.com/aspnet/aspnetcore
  --[JOB].source.branchOrCommit                                            A branch name of commit hash, e.g., my/branch, issue/1234
  --[JOB].source.initSubmodules                                            Whether to init submodules when a git repository is used, e.g., true
  --[JOB].source.localFolder                                               The local path containing the source code to upload to the server. e.g., /code/mybenchmarks

  ## .NET options

  --[JOB].source.project <filename.csproj>                                 The project file to build, relative to the source code base path, e.g., src/Benchmarks/Benchmarks.csproj
  --[JOB].sdkVersion <version>                                             The version of the .NET SDK to install and use. By default the latest available build is used.
  --[JOB].runtimeVersion <version>                                         The version of the .NET runtime to install and use. It is defined as MicrosoftNETCoreAppPackageVersion in the build arguments. By default the latest available build is used. Setting this value forces the app to be deployed as stand-alone.
  --[JOB].aspNetCoreVersion <version>                                      The version of the ASP.NET runtime to install and use. It is defined as MicrosoftAspNetCoreAppPackageVersion in the build arguments. By default the latest available build is used.  Setting this value forces the app to be deployed as stand-alone.
  --[JOB].noGlobalJson <true|false>                                        Whether to not emit any global.json file to force the .NET SDK version to use. Default is false, meaning whatever version of the .NET SDK is chosen, it will be set in a global.json file.
  --[JOB].framework <tfm>                                                  The framework version to use in case it can't be assumed from the .NET runtime version. e.g., netcoreapp3.1
  --[JOB].buildArguments <argument>                                        An argument to pass to msbuild. Can be used multiple times to define multiple values.
  --[JOB].selfContained <true|false>                                       Whether to deploy the app as stand-alone. Default is false. Is is forced to 'true' if either runtimeVersion or aspnetVersion is defined as the SDK versions would be used otherwise.
  --[JOB].useMonoRuntime <true|false>                                      Whether to use the Mono runtime.
  
  ## Docker options

  --[JOB].source.dockerFile                                                The local path to the Docker file, e.g., frameworks/Rust/actix/actix-raw.dockerfile
  --[JOB].source.dockerImageName                                           The name of the docker image to create, e.g., actix_raw
  --[JOB].source.dockerContextDirectory                                    The folder in which the Docker file is built relative to, e.g., frameworks/Rust/actix/
  --[JOB].source.dockerFetchPath                                           The path in the Docker container that contains the base path for the --fetch option, e.g., ./output
  --[JOB].source.dockerLoad                                                The path of an image to use for 'docker load', e.g, "./myimage.tar"
  --[JOB].source.dockerCommand                                             The 'docker run' command, e.g, "./startup.sh"
  --[JOB].buildArguments <argument>                                        An argument to pass to 'docker build' as a '--build-arg' value. Can be used multiple times to define multiple values.

  ## Diagnostics

  --[JOB].dotnetTrace <true|false>                                         Whether to collect a diagnostics trace using dotnet-trace. An optional profile name or list of dotnet-trace providers can be passed. e.g., true
  --[JOB].dotnetTraceProviders <profile|flags>                             An optional profile name or list of dotnet-trace providers can be passed. Default is 'cpu-sampling'. See https://github.com/dotnet/diagnostics/blob/master/documentation/dotnet-trace-instructions.md for details. e.g., Microsoft-DotNETCore-SampleProfiler, Microsoft-Windows-DotNETRuntime, gc-verbose.  Can be used multiple times to set multiple providers.
  --[JOB].options.traceOutput <filename>                                   The name of the trace file. Can be a file prefix (app will add *.DATE*.zip) , or a specific name and no DATE* will be added e.g., c:\traces\mytrace
  --[JOB].collectCounters <true|false>                                     Whether to collect dotnet counters.
  --[JOB].collectStartup <true|false>                                      Whether to includes the startup phase in the traces, i.e after the application is launched and before it is marked as ready. For a web application it means before it is ready to accept requests.

  ## Environment

  --[JOB].environmentVariables <key=value>                                 An environment variable key/value pair to assign to the process. Can be used multiple times to define multiple values.
  --[JOB].memoryLimitInBytes <bytes>                                       The amount of memory available for the process.
  --[JOB].options.requiredOperatingSystem <linux|windows|osx>              The operating system the job can only run on.
  --[JOB].options.requiredArchitecture <x64|arm64>                         The architecture the job can only run on.

  ## Debugging

  --[JOB].noClean <true|false>                                             Whether to keep the work folder on the server or not. Default is false, such that each job is cleaned once it's finished.
  --[JOB].options.fetch <true|false>                                       Whether the benchmark folder is downloaded. e.g., true. For Docker see '--[JOB].source.dockerFetchPath'
  --[JOB].options.fetchOutput <filename>                                   The name of the fetched archive. Can be a file prefix (app will add *.DATE*.zip) , or a specific name (end in *.zip) and no DATE* will be added e.g., c:\publishedapps\myApp
  --[JOB].options.displayOutput <true|false>                               Whether to download and display the standard output of the benchmark.
  --[JOB].options.displayBuild <true|false>                                Whether to download and display the standard output of the build step (works for .NET and Docker).

  ## Files
  --[JOB].options.buildFiles <filename>                                    Build files that will be copied before the application is built. Format is 'path[;destination]'. Path can be a URL. e.g., c:\images\mydockerimage.tar, c:\code\Program.cs
  --[JOB].options.outputFiles <filename>                                   Output files that will be copied in the final application folder. Format is 'path[;destination]'. Path can be a URL. e.g., c:\build\Microsoft.AspNetCore.Mvc.dll, c:\files\samples\picture.png;wwwroot\picture.png

  ## Telemetry

  --[JOB].options.disardResults <true|false>                               Whether to discard all the results from this job, for instance during a warmup job.

  # Example

  dotnet run --
    --config ..\..\..\benchmarks.compose.json 
    --scenario plaintext 

    --application.endpoints http://asp-perf-lin:5001
    --application.sdkversion 5.0.100-alpha1-015721 
    --application.dotnetTrace true 
    --application.collectCounters true 

    --load.endpoints http://asp-perf-win:5001 
    --load.source.localFolder ..\..\..\..\PipeliningClient\ 
    --load.source.project PipeliningClient.csproj 
    --load.variables.warmup 0 
    --load.variables.duration 5  
    --variables preset-headers=none 
    --variables serverUri=http://10.0.0.110:5000 
