# ASP.NET Template Benchmark App

This folder contains only a template of sample ASP.NET Core app that can be run using the [BenchmarksDriver](../../../BenchmarksDriver/README.md).

The goal is to make it very easy to reuse this template app to quickly modify it and run your own benchmark(s).

## Usage

1. Clone this repo.
2. Extend the template project with a new `Controller` and a method that you want to test.
3. Use [BenchmarksDriver](../../../BenchmarksDriver/README.md) to run the benchmark.

## Sample investigation

### Introduction

As an example we are going to use issue reported in [corefx/39600](https://github.com/dotnet/corefx/pull/39600) which says:

> CryptoConfig.CreateFromName in the dotnet core is about 4.5 times slower than the same method in .NET

Why a simple micro benchmark is not enough?

> This lock is really bad and causes contention issues when our web app is running on E20 Azure VMs.

Which means that we need to run this code in parallel to reproduce the problem.

### Git clone

The first step is to clone this repository and open the [benchmarks.sln](../../../../benchmarks.sln) in default IDE:

```cmd
git clone https://github.com/aspnet/Benchmarks
cd Benchmarks
explorer benchmarks.sln
```

### Modify the template

The next step is to extend this template project with a new controller and a method that reproduces the problem:

```cs
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;

namespace Template.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CryptoConfigController : Controller
    {
        [HttpGet]
        [Route("CreateFromName/{name}")]
        public IActionResult CreateFromName(string name)
        {
            // perform the operation more than 1 time to make sure it takes more time than the ASP.NET request handling
            for (int i = 0; i < 16; i++)
            {
                Consume(CryptoConfig.CreateFromName(name));
            }

            return Ok();
        }

        // make sure the config gets created and avoid possible dead code elimination
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Consume<T>(in T _) { }
    }
}
```

Now, if we want to benchmark this method using "RSA" argument we need to specify following argument for the [BenchmarksDriver](../../../BenchmarksDriver/README.md):

```cmd
--path /api/CryptoConfig/CreateFromName/RSA
```

### Run the benchmark

Now all we need to run the benchmark is to execute the following command (assuming that we know the benchmark server and client machine address):

```cmd
cd src/BenchmarksDriver
dotnet run -- `
    --server $secret1 --client $secret2 `
    --source ..\BenchmarksApps\Samples\Template\ `
    --project-file Template.csproj `
    --path /api/CryptoConfig/CreateFromName/RSA `
```

Sample output:

```log
RequestsPerSecond:           43,330
Max CPU (%):                 95
WorkingSet (MB):             166
Avg. Latency (ms):           5.91
Startup (ms):                314
First Request (ms):          117.24
Latency (ms):                0.61
Total Requests:              653,459
Duration: (ms)               15,080
Socket Errors:               0
Bad Responses:               0
```

### Collect trace

The next step is to collect the trace and find out where the problem really is. To do that we just extend the previous command with:

```cmd
--collect-trace
```

Sample output:

```log
Post-processing profiler trace, this can take 10s of seconds...
Trace arguments: BufferSizeMB=1024;CircularMB=1024;clrEvents=JITSymbols;kernelEvents=process+thread+ImageLoad+Profile
Downloading trace: trace.08-08-15-57-28.RPS-105K.etl.zip
```

### Identifying the problem

To identify the problem we can open the trace file with [PerfView](https://github.com/Microsoft/perfview) and [analyze it](https://adamsitnik.com/Sample-Perf-Investigation/#analysing-the-trace-file)


![Threads](./docs/img/flamegraph_not_filtered.png)

![Threads folder](./docs/img/flamegraph_filtered.png)

### Validating the fix

To validate the fix, we need to send a new version of given library using the `--output-file` command line arugment. The benchmarking infrastructure is going to publish a self-contained version of provided `Template` app and overwrite existing file with the one that we have provided. Example:


```cmd
--output-file "C:\Projects\corefx\artifacts\bin\System.Security.Cryptography.Algorithms\netcoreapp-Windows_NT-Release\System.Security.Cryptography.Algorithms.dll"
```


```log
RequestsPerSecond:           120,801
Max CPU (%):                 90
WorkingSet (MB):             166
Avg. Latency (ms):           2.27
Startup (ms):                363
First Request (ms):          92.68
Latency (ms):                0.64
Total Requests:              1,824,007
Duration: (ms)               15,100
Socket Errors:               0
Bad Responses:               0
```

The trace file captured after introducing the change shows that `CryptoConfig.CreateFromName` is not a performance bottleneck anymore:

![Threads folder](./docs/img/flamegraph_filtered_after.png)
