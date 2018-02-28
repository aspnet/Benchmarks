# ASP.NET Benchmark Driver

## Usage

```
Usage: BenchmarksDriver [options]

Options:
  -?|-h|--help           Show help information
  -c|--client            URL of benchmark client
  -s|--server            URL of benchmark server
  -q|--sql               Connection string of SQL Database to store results
  -v|--verbose           Verbose output
  --session              A logical identifier to group related jobs.
  --description          The description of the job.
  -i|--iterations        The number of iterations.
  -x|--exclude           The number of best and worst and jobs to skip.  
  --database             The type of database to run the benchmarks with (PostgreSql, SqlServer or MySql). Default is PostgreSql.
  -f|--connectionFilter  Assembly-qualified name of the ConnectionFilter
  --kestrelThreadCount   Maps to KestrelServerOptions.ThreadCount.
  -n|--scenario          Benchmark scenario to run
  -m|--scheme            Scheme (http or https).  Default is http.
  -o|--source            Source dependency. Format is 'repo@branchOrCommit'. Repo can be a full URL, or a short name under https://github.com/aspnet.
  -w|--webHost           WebHost (e.g., KestrelLibuv, KestrelSockets, HttpSys). Default is KestrelSockets.
  --aspnetCoreVersion    ASP.NET Core packages version (Current, Latest, or custom value). Current is the latest public version, Latest is the currently developped one. Default is Latest (2.1.0-*).
  --runtimeVersion       .NET Core Runtime version (Current, Latest, or custom value). Current is the latest public version, Latest is the currently developped one. Default is Latest (2.1.0-*).
  --arguments            Arguments to pass to the application. (e.g., "--raw true")
  --port                 The port used to request the benchmarked application. Default is 5000.
  -r|--repository        Git repository containing the project to test.
  --projectFile          Relative path of the project to test in the repository. (e.g., "src/Benchmarks/Benchmarks.csproj)"
  --useRuntimeStore      Runs the benchmarks using the runtime store if available.
  --timeout              The max delay to wait to the job to run. Default is 00:05:00.
  --outputFile           Output file attachment. Format is 'path[;destination]'. FilePath can be a URL. e.g., "--outputFile c:\build\Microsoft.AspNetCore.Mvc.dll", "--outputFile c:\files\samples\picture.png;wwwroot\picture.png"
  --runtimeFile          Runtime file attachment. Format is 'path[;destination]', e.g., "--runtimeFile c:\build\System.Net.Security.dll"  
  --properties           Key value pairs of properties specific to the client running. e.g., "Threads=16;PipelineDepth=16"
  --clientName           Name of client to use for testing, e.g. 'wrk'.
  --connections          Number of connections used by client. Default is 256.
  --duration             Duration of test in seconds. Default is 15.
  --warmup               Duration of warmup in seconds. Default is 15.
  --header               Header added to request.
  --headers              Default set of HTTP headers added to request (None, Plaintext, Json, Html). Default is Html.
  --method               HTTP method of the request. Default is GET.
  --path                 Relative URL where the client should send requests.
  --querystring          Querystring to add to the requests. (e.g., "?page=1")
  -j|--jobs              The path or url to the jobs definition.
  --collect-trace        Collect a PerfView trace. Optionally set custom arguments. e.g., BufferSize=256;InMemoryCircularBuffer
  --trace-output         An optional location to download the trace file to, e.g., --trace-output c:\traces
  --before-shutdown      An endpoint to call before the application has shut down.
  -sp|--span             The time during which the client jobs are repeated, in 'HH:mm:ss' format. e.g., 48:00:00 for 2 days
  -t|--table             Table name of the SQL Database to store results
  --no-crossgen          Disables Ready To Run.
  -e|--env               Defines custom envrionment variables to use with the benchmarked application e.g., -e KEY=VALUE -e A=B
```

### Examples

Running a job without any job definition file

```powershell
dotnet run -c release 
    --server "http://localhost:5001" 
    --client "http://10.0.75.2:5002" 
    --repository "https://github.com/sebastienros/FrameworkBenchmarks.git" 
    --projectFile "frameworks/CSharp/aspnetcore/Benchmarks/Benchmarks.csproj" 
    --path "/plaintext" 
    --connections 256 
    --clientName "wrk" 
    --properties "Threads=16;PipelineDepth=16" 
    --duration 15 
    --headers Plaintext 
```

Running the "Plaintext" job defined in the __benchmaks.json__ file, targeting 2.0.0

```powershell
dotnet run -c release --server "http://localhost:5001" --client "http://10.0.75.2:5002" -n Plaintext -j "C:\Benchmarks\benchmarks.json" --aspnetCoreVersion Current
```

## Job definition format

This file contains a list of predefined named jobs and their properties. When a job is named `"Default"` its properties will be applied to any other job in the file.
Also if no named job is requested on the command line, the _default_ job will be used.

### Example

```json
{
    "Default": {
        "ClientName": "wrk",
        "Properties": {
            "Threads": "32",
            "ScriptName": "pipline",
            "PipelineDepth" : 16
        },
        "PresetHeaders": "Plaintext", // None, Html, Plaintext or Json
        "Headers": { 
            "Foo": "Bar"
        },
        "Source": {
            "Repository": "https://github.com/aspnet/benchmarks.git",
            "BranchOrCommit": "dev",
            "Project": "src/Benchmarks/Benchmarks.csproj"
        },

        "Connections": 256,
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