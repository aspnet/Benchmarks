# ASP.NET Benchmark Driver

## Usage

```
Usage: BenchmarksDriver [options]

Options:
  -?|-h|--help                    Show help information
  -c|--client                     URL of benchmark client
  -s|--server                     URL of benchmark server
  -q|--sql                        Connection string of SQL Database to store results.
  -v|--verbose                    Verbose output
  -f|--connectionFilter           Assembly-qualified name of the ConnectionFilter
  -j|--jobs                       The path or url to the jobs definition.
  --kestrelThreadCount            Maps to KestrelServerOptions.ThreadCount.
  --kestrelThreadPoolDispatching  Maps to InternalKestrelServerOptions.ThreadPoolDispatching.
  --kestrelTransport              Kestrel's transport (Libuv or Sockets). Default is Libuv.
  -n|--scenario                   Benchmark scenario to run
  -m|--scheme                     Scheme (http or https).  Default is http.
  -o|--source                     Source dependency. Format is 'repo@branchOrCommit'. Repo can be a full URL, or a short name under https://github.com/aspnet.
  -w|--webHost                    WebHost (Kestrel or HttpSys). Default is Kestrel.
  --aspnetCoreVersion             ASP.NET Core version (2.0.0, 2.0.1 or 2.1.0-*). Default is 2.1.0-*.
  --session                       A logical identifier to group related jobs.
  --description                   The description of the job.
  --clientThreads                 Number of threads used by client. Default is 32.
  --connections                   Number of connections used by client. Default is 256.
  --duration                      Duration of test in seconds. Default is 15.
  --headers                       Predefined set of headers (Plaintext, Json, Html, None). Default is Html.
  --header                        Header added to request. (e.g., "Host=localhost")
  --method                        HTTP method of the request. Default is GET.
  --pipelineDepth                 Depth of pipeline used by client.
  --script                        Name of the script used by wrk.
  --path                          Relative URL where the client should send requests.
  --querystring                   Querystring to add to the requests. (e.g., "?page=1")
  --arguments                     Arguments to pass to the application. (e.g., "--raw true")
  --repository                    Project repository. Format is 'repo@branchOrCommit'. Repo can be a full URL, or a short name under https://github.com/aspnet.
  --project                       Relative path of the project to test in the repository. (e.g., "src/Benchmarks/Benchmarks.csproj)
  --useRuntimeStore               Runs the benchmarks using the runtime store if available.
```

### Examples

Running a job without any job definition file

```powershell
dotnet run -c release 
    --server "http://localhost:5001" 
    --client "http://10.0.75.2:5002" 
    --repository "https://github.com/sebastienros/FrameworkBenchmarks.git" 
    --branchOrCommit "benchmarks" 
    --projectFile "frameworks/CSharp/aspnetcore/Benchmarks/Benchmarks.csproj" 
    --path "/plaintext" 
    --connections 256 
    --clientThreads 16 
    --duration 15 
    --pipelineDepth 16 
    --headers Plaintext 
```

Running the "Plaintext" job defined in the __benchmaks.json__ file, targeting 2.0.0

```powershell
dotnet run -c release --server "http://localhost:5001" --client "http://10.0.75.2:5002" -n Plaintext -j "C:\Benchmarks\benchmarks.json" --aspnetCoreVersion 2.0.0
```

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
            "BranchOrCommit": "dev",
            "Project": "src/Benchmarks/Benchmarks.csproj"
        },

        "Connections": 256,
        "Threads": 32,
        "Duration": 15,

        "AspNetCoreVersion": "2.1.0-*",
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

`Database`: either None, PostgreSql, SqlServer or MySql
`ConnectionString`: The connection string to use to run the benchmark application

## MSBUILD parameters

When the benchmarked application is built, `msbuild` is invoked with a set of parameters that can be used to customize the build.

```
Usage: BenchmarksDriver [options]

Options:
  BenchmarksAspNetCoreVersion                       Set to the value of --aspnetCoreVersion
  BenchmarksNETStandardImplicitPackageVersion       Set to the value of --aspnetCoreVersion
  BenchmarksNETCoreAppImplicitPackageVersion        Set to the value of --aspnetCoreVersion
  BenchmarksRuntimeFrameworkVersion                 Set to 2.0.0
```

### Sample project file 

This demonstrate how `msbuild` parameters can be used in a benchmarked application

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>netcoreapp2.0</TargetFramework>
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