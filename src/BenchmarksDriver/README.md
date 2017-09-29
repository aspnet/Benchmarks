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
  --clientThreads                 Number of threads used by client.
  --connections                   Number of connections used by client.
  --duration                      Duration of test in seconds.
  --header                        Header added to request. (e.g., "Host=localhost")
  --method                        HTTP method of the request. Default is GET.
  --pipelineDepth                 Depth of pipeline used by client.
  --script                        Name of the script used by wrk.
  --path                          Relative URL where the client should send requests.
  --querystring                   Querystring to add to the requests. (e.g., "?page=1")
  --arguments                     Arguments to pass to the application. (e.g., "--raw true")
  --repository                    Git repository containing the project to test.
  --branchOrCommit                Branch name of commit hash to checkout.
  --project                       Relative path of the project to test in the repository. (e.g., "src/Benchmarks/Benchmarks.csproj)
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
    --header "Host=localhost" 
    --header "Accept=text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7" 
    --header "Connection=keep-alive"
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
        "Headers": { 
            "Host": "localhost",
            "Accept": "text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7",
            "Connection": "keep-alive"
        },
        "Source": {
            "Repository": "https://github.com/aspnet/benchmarks.git",
            "BranchOrCommit": "dev",
            "Project": "src/Benchmarks/Benchmarks.csproj"
        },

        "Connections": 256,
        "Threads": 32,
        "Duration": 15
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

This definition contains three custom job plus the _default_ one.