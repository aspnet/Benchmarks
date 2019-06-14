# ASP.NET Benchmark Driver

## Usage

```
Usage: BenchmarksDriver [options]

Options:
  -?|-h|--help           Show help information
  -c|--client            URL of benchmark client
  -s|--server            URL of benchmark server
  -q|--sql               Connection string of SQL Database to store results
  --clientName           Name of client to use for testing, e.g. Wrk
  -v|--verbose           Verbose output
  --quiet                Quiet output, only the results are displayed
  --session              A logical identifier to group related jobs.
  --description          The description of the job.
  -i|--iterations        The number of iterations.
  -x|--exclude           The number of best and worst jobs to skip.  
  --database             The type of database to run the benchmarks with (PostgreSql, SqlServer or MySql). Default is None.
  -cf|--connectionFilter Assembly-qualified name of the ConnectionFilter
  --kestrelThreadCount   Maps to KestrelServerOptions.ThreadCount.
  -n|--scenario          Benchmark scenario to run
  -m|--scheme            Scheme (http, https, h2, h2c). Default is http.
  -w|--webHost           WebHost (e.g., KestrelLibuv, KestrelSockets, HttpSys). Default is KestrelSockets.
  --aspnet               ASP.NET Core packages version (Current, Latest, or custom value). Current is the latest public version (2.0.*), Latest is the currently developped one. Default is Latest (2.1-*).
  --dotnet               .NET Core Runtime version (Current, Latest, Edge or custom value). Current is the latest public version, Latest is the one enlisted, Edge is the latest available. Default is Latest (2.1.0-*).
  -a|--arg               Argument to pass to the application. (e.g., --arg --raw=true --arg "single_value")
  --no-arguments         Removes any predefined arguments from the server application command line.
  --port                 The port used to request the benchmarked application. Default is 5000.
  --ready-text           The text that is displayed when the application is ready to accept requests. (e.g., "Application started.")
  -r|--repository        Git repository containing the project to test.
  -b|--branch            Git branch to checkout.
  -h|--hash              Git hash to checkout.
  --projectFile          Relative path of the project to test in the repository. (e.g., "src/Benchmarks/Benchmarks.csproj")
  --init-submodules      When set will init submodules on the repository.
  -df|--docker-file      File path of the Docker script. (e.g, "frameworks/CSharp/aspnetcore/aspcore.dockerfile")
  -dc|--docker-context   Docker context directory. Defaults to the Docker file directory. (e.g., "frameworks/CSharp/aspnetcore/")
  -di|--docker-image     The name of the Docker image to create. If not net one will be created from the Docker file name. (e.g., "aspnetcore21")
  --runtime-store        Runs the benchmarks using the runtime store (2.0) or shared aspnet framework (2.1).
  --outputFile           Output file attachment. Format is 'path[;destination]'. FilePath can be a URL. e.g., "--outputFile c:\build\Microsoft.AspNetCore.Mvc.dll", "--outputFile c:\files\samples\picture.png;wwwroot\picture.png"
  --output-archive       Output archive attachment. Format is 'path[;destination]'. FilePath can be a URL. e.g., "--output-archive c:\build\Microsoft.AspNetCore.Mvc.zip\", "--output-archive http://raw/github.com/pictures.zip;wwwroot\pictures"
  -src|--source          Local folder containing the project to test.
  --client-threads       Number of threads used by client. Default is 32.
  --timeout              Timeout for client connections. e.g., 2s
  --connections          Number of connections used by client. Default is 256.
  --duration             Duration of test in seconds. Default is 15.
  --warmup               Duration of warmup in seconds. Default is 15. 0 disables the warmup and is equivalent to --no-warmup.
  --no-warmup            Disables the warmup phase.
  --header               Header added to request.
  --headers              Default set of HTTP headers added to request (None, Plaintext, Json, Html). Default is Html.
  --method               HTTP method of the request. Default is GET.
  -p|--properties        Key value pairs of properties specific to the client running. e.g., -p ScriptName=pipeline -p PipelineDepth=16
  --script               Wrk script path. File path can be a URL. e.g., --script c:\scripts\post.lua
  --path                 Relative URL where the client should send requests.
  --querystring          Querystring to add to the requests. (e.g., "?page=1")
  -j|--jobs              The path or url to the jobs definition.
  --collect-trace        Collect a PerfView trace. Optionally set custom arguments. e.g., BufferSize=256;InMemoryCircularBuffer
  --collect-startup      Includes the startup phase in the trace.
  --collect-counters     Collect event counters.
  --trace-output         Can be a file prefix (app will add *.DATE.RPS*.etl.zip) , or a specific name (end in *.etl.zip) and no DATE.RPS* will be added e.g. --trace-output c:\traces\myTrace
  --trace-arguments      Arguments used when collecting a PerfView trace, e.g., Providers=.NETTasks:0:0 (Defaults are BufferSizeMB=1024;CircularMB=1024 on Windows and -collectsec 10 on Linux)
  --enable-eventpipe     Enables EventPipe perf collection.
  --eventpipe-arguments  EventPipe configuration. Defaults to "Microsoft-DotNETCore-SampleProfiler:1:5,Microsoft-Windows-DotNETRuntime:4c14fccbd:5"
  --before-shutdown      An endpoint to call before the application has shut down.
  -sp|--span             The time during which the client jobs are repeated, in 'HH:mm:ss' format. e.g., 48:00:00 for 2 days
  -md|--markdown         Formats the output in markdown
  -t|--table             Table name of the SQL Database to store results
  --no-crossgen          Disables Ready To Run (aka crossgen), in order to use the JITed version of the assemblies.
  --tiered-compilation   Enables tiered-compilation.
  --self-contained       Publishes the .NET Core runtime with the application.
  -e|--env               Defines custom envrionment variables to use with the benchmarked application e.g., -e KEY=VALUE -e A=B
  -b|--build-arg         Defines custom build arguments to use with the benchmarked application e.g., -b "/p:foo=bar" --build-arg "quiet"
  --windows-only         Don't execute the job if the server is not running on Windows
  --linux-only           Don't execute the job if the server is not running on Linux
  --save                 Stores the results in a local file, e.g. --save baseline. If the extension is not specified, '.bench.json' is used.
  --diff                 Displays the results of the run compared to a previously saved result, e.g. --diff baseline. If the extension is not specified, '.bench.json' is used.
  -d|--download          Downloads specific server files. This argument can be used multiple times. e.g., -d "published/wwwroot/picture.png"
  --fetch                Downloads the published application locally.
  --fetch-output         Can be a file prefix (app will add *.DATE*.zip) , or a specific name (end in *.zip) and no DATE* will be added e.g. --fetch-output c:\publishedapps\myApp
  -wf|--write-file       Writes the results to a file named "results.md". NB: Use the --description argument to differentiate multiple results.
  --display-output       Displays the standard output from the server job.
  --benchmarkdotnet      Runs a BenchmarkDotNet application. e.g., --benchmarkdotnet Benchmarks.LabPerf.Md5VsSha256
  --console              Runs the benchmarked application as a console application, such that no client is used and its output is displayed locally.
  --server-timeout       Timeout for server jobs. e.g., 00:05:00
  --framework            TFM to use if automatic resolution based runtime should not be used. e.g., netcoreapp2.1
  --sdk                  SDK version to use
  --initialize           A script to run before the application starts, e.g. "du", "/usr/bin/env bash dotnet-install.sh"
  --clean                A script to run after the application has stopped, e.g. "du", "/usr/bin/env bash dotnet-install.sh"
  -mem|--memory          The amount of memory available for the process, e.g. -mem 64mb, -mem 1gb. Supported units are (gb, mb, kb, b or none for bytes).

Properties of the Wrk client

  ScriptName             Name of the script used by wrk.
  PipelineDepth          Depth of pipeline used by clients.
  Scripts                List of paths or urls to lua scripts to use, sperater by semi-colons (;).

Properties of the SignalR client
  HubProtocol            Name of the hub protocol to be used between client and server.
  TransportType          Name of the transport to communicate over.
  LogLevel               LogLevel name for SignalR connections to use. e.g. 'Trace' or 'Warning'.
  CollectLatency         Turns on collection of detailed latency, used for Percentiles, by default we just collect average Latency.
  SendDelay		         Specifies the delay(in seconds) between sends for the echo idle scenario.

Properties of the Wait client

  None                   By default the client will use the value of the `--duration` property.

Properties of the H2Load client

  Streams                Max concurrent streams to issue per session. When http/1.1 is used, this specifies the number of HTTP pipelining requests in-flight. Default is 1.

Properties of the Bombardier client

  rate                   Rate limit in requests per second
  requests               Number of requests (substitutes --duration)

```

### Examples

#### Using a local job definition file

Running the "Plaintext" job defined in the `benchmaks.json` file, targeting the latest stable release of ASP.NET Core.

```powershell
dotnet run -c release --server "http://localhost:5001" --client "http://10.0.75.2:5002" -n Plaintext -j "C:\Benchmarks\benchmarks.json" --aspnetCoreVersion Current
```

Any path can point to a raw online resource, like `https://github.com/aspnet/Benchmarks/blob/master/src/Benchmarks/benchmarks.plaintext.json`

#### Running a job without any job definition file

A job definition file is useful for executing jobs repeatedly, but all its properties can be set or redefined on the command line.

```powershell
dotnet run -c release 
    --server "http://localhost:5001" 
    --client "http://10.0.75.2:5002" 
    --repository "https://github.com/sebastienros/FrameworkBenchmarks.git" 
    --projectFile "frameworks/CSharp/aspnetcore/Benchmarks/Benchmarks.csproj" 
    --path "/plaintext" 
    --connections 256 
    --clientThreads 16 
    --duration 15 
    --pipelineDepth 16 
    --headers Plaintext 
```

#### Customizing a PerfView trace

The following example also adds the GC information:

`--collect-trace --trace-arguments "clrEvents=JITSymbols+GC"`

## Job definition format

This file contains a list of predefined named jobs and their properties. When a job is named `"Default"` its properties will be applied to any other job in the file.
Also if no named job is requested on the command line, the _default_ job will be used.

### Example

```json
{
    "Default": {
        "ScriptName": "pipeline",
        "PipelineDepth" : 16,
        "PresetHeaders": "Plaintext", // None, Html, Plaintext or Json
        "Headers": { 
            "Foo": "Bar"
        },
        "Source": {
            "Repository": "https://github.com/aspnet/benchmarks.git",
            "BranchOrCommit": "master",
            "Project": "src/Benchmarks/Benchmarks.csproj"
        },

        "Connections": 256,
        "Threads": 32,
        "Duration": 15,

        "AspNetCoreVersion": "Latest",
        "RuntimeVersion": "Latest",
        "Port": 8081
    },
    "Plaintext": {
        "Path": "/plaintext"
    },
    "Json": {
        "Path": "/json"
    },
    "ResponseCachingPlaintextRequestNoCache" :{
        "Path": "/responsecaching/plaintext/requestnocache",
        "Headers": {
            "Cache-Control": "no-cache"
        }
    },    
}
```

This definition contains three custom jobs plus the _default_ one.

## Database connection

When the benchmarked application is executed, the following environment variables are set:

`Database`: either None, PostgreSql, SqlServer, MySql or MongoDb
`ConnectionString`: The connection string to use to run the benchmark application

## MSBUILD parameters

When the benchmarked application is built, `msbuild` is invoked with a set of parameters that can be used to customize the build.

```
Usage: BenchmarksDriver [options]

Options:
  BenchmarksAspNetCoreVersion                       Set to the value of --aspnetCoreVersion
  BenchmarksNETStandardImplicitPackageVersion       Set to the value of --aspnetCoreVersion
  BenchmarksNETCoreAppImplicitPackageVersion        Set to the value of --aspnetCoreVersion
  BenchmarksRuntimeFrameworkVersion                 Set to the value of --runtimeVersion (e.g., 2.0.3, 2.1.0-*)
  BenchmarksTargetFramework                         Set to netcoreapp2.0 or netcoreapp2.1
```

### Sample project file 

This demonstrate how `msbuild` parameters can be used in a benchmarked application

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>$(BenchmarksTargetFramework)</TargetFramework>
    <OutputType>Exe</OutputType>
    <MvcRazorCompileOnPublish>true</MvcRazorCompileOnPublish>
    <NETCoreAppImplicitPackageVersion>$(BenchmarksNETCoreAppImplicitPackageVersion)</NETCoreAppImplicitPackageVersion>
    <RuntimeFrameworkVersion>$(BenchmarksRuntimeFrameworkVersion)</RuntimeFrameworkVersion>
  </PropertyGroup>

  <PropertyGroup Condition="'$(BenchmarksAspNetCoreVersion)' == '2.1.0-*'">
    <DefineConstants>$(DefineConstants);DOTNET210</DefineConstants>
  </PropertyGroup>

  <PropertyGroup Condition="'$(BenchmarksAspNetCoreVersion)' == '2.0.0' or '$(BenchmarksAspNetCoreVersion)' == '2.0.1'">
    <DefineConstants>$(DefineConstants);DOTNET200</DefineConstants>
  </PropertyGroup>
  
  <ItemGroup>
    <None Update="wwwroot/**" CopyToOutputDirectory="PreserveNewest" />
    <None Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc" Version="$(BenchmarksAspNetCoreVersion)" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Razor.ViewCompilation" Version="$(BenchmarksAspNetCoreVersion)" />
    <PackageReference Include="Microsoft.AspNetCore.ResponseCaching" Version="$(BenchmarksAspNetCoreVersion)" />
    <PackageReference Include="Microsoft.AspNetCore.Server.HttpSys" Version="$(BenchmarksAspNetCoreVersion)" />
    <PackageReference Include="Microsoft.AspNetCore.Server.Kestrel" Version="$(BenchmarksAspNetCoreVersion)" />
    <PackageReference Include="Microsoft.AspNetCore.Server.Kestrel.Https" Version="$(BenchmarksAspNetCoreVersion)" />
    <PackageReference Include="Microsoft.AspNetCore.StaticFiles" Version="$(BenchmarksAspNetCoreVersion)" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="$(BenchmarksAspNetCoreVersion)" />
    <PackageReference Include="Microsoft.Extensions.Configuration.CommandLine" Version="$(BenchmarksAspNetCoreVersion)" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="$(BenchmarksAspNetCoreVersion)" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="$(BenchmarksAspNetCoreVersion)" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="$(BenchmarksAspNetCoreVersion)" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="$(BenchmarksAspNetCoreVersion)" />
  </ItemGroup>

  <ItemGroup Condition="'$(BenchmarksAspNetCoreVersion)' == '2.1.0-*'">
    <PackageReference Include="Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets" Version="$(BenchmarksAspNetCoreVersion)" />
  </ItemGroup>

</Project>
```
