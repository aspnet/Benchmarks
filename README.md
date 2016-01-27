# Benchmarks
Benchmarks for ASP.NET 5 including (but not limited to) scenarios from the [TechEmpower Web Framework Benchmarks](https://www.techempower.com/benchmarks/).

# Running the benchmarks

The benchmark repo is set up to work against the latest sources (i.e. not packages from nuget.org) for ASP.NET 5 so make sure you read through the following details to help you get started.

The ASP.NET 5 benchmarks server application itself is in the `./src/Benchmarks` folder. The `./experimental` folder contains various experimental projects that aren't themselves part of the benchmarks.

## The scenarios
Following are the details of each of the scenarios the server application contains implementations for and thus can be benchmarked:

| url | Name | Description |
| :--- | :--- | :--- |
| /plaintext | Plaintext | *From https://www.techempower.com/benchmarks/* This test is an exercise of the request-routing fundamentals only, designed to demonstrate the capacity of high-performance platforms in particular. Requests will be sent using HTTP pipelining. |
| /json | JSON | *From https://www.techempower.com/benchmarks/* This test exercises the framework fundamentals including keep-alive support, request routing, request header parsing, object instantiation, JSON serialization, response header generation, and request count throughput. |
| /mvc/plaintext | MVC Plaintext | As for the plaintext test above, but using routing & MVC. |
| /mvc/json | MVC JSON | As for the json test above, but using routing & MVC. |
| /mvc/view | MVC Plain View | As for the plaintext test above, but using routing & MVC plus rendering the result via a Razor view. |
| /db/raw | Single Query Raw | *From https://www.techempower.com/benchmarks/* This test exercises the framework's object-relational mapper (ORM), random number generator, database driver, and database connection pool. |
| /db/ef | Single Query EF | As for the single query raw test above but using Entity Framework 7 as the data-access library/ORM. |

The addition of more scenarios is pending.

## Setting up the web server
You can run the benchmarks application server on Windows, OSX or Linux.

1. Follow the [instructions on the ASP.NET 5 docs site](https://docs.asp.net/en/latest/getting-started/index.html) to get the appropriate runtime and tooling pieces for your chosen platform installed.

1. Clone this repo to the server.

1. Navigate to the `./src/Benchmarks` directory under this repo and run the following command to install the latest version of the ASP.NET 5 runtime for .NET Core CLR on x64:
   ```
   dnvm install latest -r coreclr -arch x64 -u
   ```

   *Note: You can also install and use flavors of the runtime for x86 (`-arch x86`) and full CLR (`-r clr`) on Windows if you so desire. Just type `dnvm` in the console for more details on installing and selecting versions of the runtime to use.*

1. Run the following command to restore package depedencies for the server application:
   ```
   dnu restore
   ```

1. Finally, start the server application with the following command:
   ```
   dnx --configuration Release run
   ```

*Note: You may need to open port 5001 for external traffic in your firewall for the server to successfully run*

## Generating Load
It's best to generate load from a completely separate machine from the server if you can, to avoid resource contention during the test.

We use the [wrk](https://github.com/wg/wrk) load generation tool to generate the load for our benchmark runs. It's the best tool we've found for the job and supports HTTP pipelining (used by the plaintext scenario) via its scripting interface. Wrk will only run from a Linux machine however, so if you must use Windows, try using [ab](https://httpd.apache.org/docs/2.2/programs/ab.html) (Apache Bench). You can [dowload ab for Windows from here](http://download.nextag.com/apache/httpd/binaries/win32/#down). 

You'll need to clone the [wrk repo](https://github.com/wg/wrk) on your load generation machine and follow [their instructions to build it](https://github.com/wg/wrk/wiki/Installing-Wrk-on-Linux).

Here's a sample wrk command to generate load for the JSON scenario. This run is using 256 connections across 32 client threads for a duration of 10 seconds.
```
wrk -c 256 -t 32 -d 10 http://10.0.0.100:5001/json
```

To generate pipelined load for the plaintext scenario, use the following command, assuming your CWD is the root of this repo and wrk is on your path. The final argument after the `--` is the desired pipeline depth. We always run the plaintext scenario at a pipeline depth of 16, [just like the Techempower Benchmarks](https://github.com/TechEmpower/FrameworkBenchmarks/blob/6594d32db618c6ca65e0106c5adf2671f7b63654/toolset/benchmark/framework_test.py#L640).
```
wrk -c 256 -t 32 -d 10 -s ./scripts/pipeline.lua http://10.0.0.100:5001/plaintext -- 16
```

*Note you may want to tweak the number of client threads (the `-t` arg) being used based on the specs of your load generation machine.*

## Running the database scenarios
The database scenarios are currently disabled in the application by default. To enable them, ensure the `EnableDbTests` configuration property is set to "true" when starting the server application. The easiest way to do this is to set an environment variable in the terminal session you're launching the application from, e.g. from PowerShell `$env:EnableDbTests="true"`.

We're still building out the database scenarios including the infrastructure for running them against various database servers and using various data-access libraries. Stay tuned for more details soon.

# Details of our perf lab

## Environment
We're using the following physical machines to perform these tests:

| Name | OS | Role | CPU | RAM | NIC | Notes |
| ---- | --- | ---- | --- | --- | --- | ----- |
| perfsvr | Windows Server 2012 R2 | Web Server | [Xeon E5-1650](http://ark.intel.com/products/64601/Intel-Xeon-Processor-E5-1650-12M-Cache-3_20-GHz-0_0-GTs-Intel-QPI) | 32 GB | [Intel® Ethernet Converged Network Adapter X540-T1 10GbE](http://ark.intel.com/products/58953/Intel-Ethernet-Converged-Network-Adapter-X540-T1) |
| perfsvr2 | Ubuntu 14.04 LTS | Web Server & Load Generator | [Xeon E5-1650](http://ark.intel.com/products/64601/Intel-Xeon-Processor-E5-1650-12M-Cache-3_20-GHz-0_0-GTs-Intel-QPI) | 32 GB | [Intel® Ethernet Converged Network Adapter X540-T1 10GbE](http://ark.intel.com/products/58953/Intel-Ethernet-Converged-Network-Adapter-X540-T1) |
| perf02 | Windows Server 2012 R2 | Load Generator | [Xeon W3550](http://ark.intel.com/products/39720/Intel-Xeon-Processor-W3550-8M-Cache-3_06-GHz-4_80-GTs-Intel-QPI) | 24 GB | [Intel® Ethernet Converged Network Adapter X540-T1 10GbE](http://ark.intel.com/products/58953/Intel-Ethernet-Converged-Network-Adapter-X540-T1) |
| perf03 | Ubuntu 14.04 LTS | Load Generator | [Xeon W3550](http://ark.intel.com/products/39720/Intel-Xeon-Processor-W3550-8M-Cache-3_06-GHz-4_80-GTs-Intel-QPI) | 12 GB | [Intel® Ethernet Converged Network Adapter X540-T1 10GbE](http://ark.intel.com/products/58953/Intel-Ethernet-Converged-Network-Adapter-X540-T1) |

The machines are connected to an 8-port [Netgear XS708E](http://www.netgear.com/business/products/switches/unmanaged-plus/10g-plus-switch.aspx) 10-Gigabit switch, that is isolated from the rest of the corporate network (the machines are all [multihomed](https://en.wikipedia.org/wiki/Multihoming)).

## Load Generation
We're using [wrk](https://github.com/wg/wrk) to generate load from one of our Linux boxes (usually perf03).

# Results
For each stack, variations of the load parameters and multiple runs are tested and the highest result is recorded. Detailed results are tracked in the [results spreadsheet](https://github.com/aspnet/benchmarks/blob/master/results/Results.xlsx).

## Experimental Baselines

These are server experiments that are intended to measure the non-HTTP overload of different technology stacks and approaches. These generally aren't real HTTP servers but rather TCP servers that special case replying to any HTTP-looking request with a fixed HTTP response.

| Stack | Server |  Req/sec | Load Params | Impl | Observations |
| ----- | ------ | -------- | ----------- | ---- | ------------ |
| Hammer (raw HTTP.SYS) | perfsvr | ~280,000 | 32 threads, 512 connections | C++ directly on HTTP.SYS | CPU is 100% |
| Hammer (raw HTTP.SYS) | perfsvr | ~460,000 | 32 threads, 256 connections, pipelining 16 deep | C++ directly on HTTP.SYS | CPU is 100% |
| libuv C# | perfsvr | 300,507 | 12 threads, 1024 connections | Simple TCP server, load spread across 12 ports (port/thread/CPU) | CPU is 54%, mostly in kernel mode |
| libuv C# | perfsvr | 2,379,267 | 36 threads, 288 connections, pipelining 16 deep | Simple TCP server, load spread across 12 ports (port/thread/CPU) | CPU is 100%, mostly in user mode |
| RIO C# | perfsvr | ~5,905,000 | 32 threads, 512 connections, pipelining 16 deep | Simple TCP server using Windows Registered IO (RIO) via P/Invoke from C# | CPU is 100%, 95% in user mode |

## Plain Text

Similar to the plain text benchmark in the TechEmpower tests. Intended to highlight the HTTP efficiency of the server & stack. Implementations are free to cache the response body aggressively and remove/disable components that aren't required in order to maximize performance.

| Stack | Server |  Req/sec | Load Params | Impl | Observations |
| ----- | ------ | -------- | ----------- | ---- | ------------ |
| ASP.NET 4.6 | perfsvr | 57,843 | 32 threads, 256 connections | Generic reusable handler, unused IIS modules removed | CPU is 100%, almost exclusively in user mode |
| IIS Static File (kernel cached) | perfsvr | 276,727 | 32 threads, 512 connections | hello.html containing "HelloWorld" | CPU is 36%, almost exclusively in kernel mode |
| IIS Static File (non-kernel cached) | perfsvr |231,609 | 32 threads, 512 connections | hello.html containing "HelloWorld" | CPU is 100%, almost exclusively in user mode |
| NodeJS | perfsvr | 106,479 | 32 threads, 256 connections | The actual TechEmpower NodeJS app | CPU is 100%, almost exclusively in user mode |
| NodeJS | perfsvr2 (Linux) | 127,017 | 32 threads, 512 connections | The actual TechEmpower NodeJS app | CPU is 100%, almost exclusively in user mode |
| ASP.NET 5 on Kestrel | perfsvr | 168,005 | 32 threads, 256 connections | Middleware class, multi IO thread | CPU is 100% |
| Scala - Plain | perfsvr | 176,509 | 32 threads, 1024 connections | The actual TechEmpower Scala Plain plaintext app | CPU is 68%, mostly in kernel mode |
| Netty | perfsvr | 447,993 | 32 threads, 256 connections | The actual TechEmpower Netty app | CPU is 100% |

## Plain Text with HTTP Pipelining

Like the Plain Text scenario above but with HTTP pipelining enabled at a depth of 16. Only stacks/servers that show an improvement with pipelining are included.

| Stack | Server |  Req/sec | Load Params | Impl | Observations |
| ----- | ------ | -------- | ----------- | ---- | ------------ |
| NodeJS | perfsvr | 147,554 | 32 threads, 256 connections | The actual TechEmpower NodeJS app | CPU is 100%, almost exclusively in user mode |
| NodeJS | perfsvr2 (Linux) | 173,641 | 32 threads, 512 connections | The actual TechEmpower NodeJS app | CPU is 100% |
| ASP.NET 5 on Kestrel | perfsvr | 743,123 | 32 threads, 1,024 connections | Middleware class, multi IO thread | CPU is 100% |
| ASP.NET 5 on Kestrel | perfsvr2 (Linux) | 409,856 | 32 threads, 256 connections | Middleware class, single IO thread | CPU is 75% |
| Scala | perfsvr | 1,514,942 | 32 threads, 1024 connections | The actual TechEmpower Scala plaintext app | CPU is 100%, 70% in user mode |
| Netty | perfsvr | 2,808,515 | 32 threads, 256 connections | The actual TechEmpower Netty app | CPU is 100% |

This project is part of ASP.NET 5. You can find samples, documentation and getting started instructions for ASP.NET 5 at the [Home](https://github.com/aspnet/home) repo.


