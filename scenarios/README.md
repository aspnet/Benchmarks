# Benchmark Scenarios

This folder contains benchmarks that represent common scenarios to look at for the .NET team.

Continuous benchmarking results are available on [this PowerBI dashboard](https://aka.ms/aspnet/benchmarks).

> Note: The dashboard's navigation is at the bottom of the page, accessible by clicking between the Next/Previous page links.

## Requirements

These jobs can be executed using the .NET Crank global tool. 
[.NET Core](<http://dot.net>) is required to install the global tool.

Install `crank` with the following command:

```
dotnet tool install Microsoft.Crank.Controller --version "0.2.0-*" --global
```

Alternatively, update `crank` with the following command:

```
dotnet tool update Microsoft.Crank.Controller --version "0.2.0-*" --global
```

## Profiles

Each profile defines a set of machines, private IPs and ports that are used to run a benchmark.

| Profile        | Arch         | OS           | Proc |
| :------------- | :----------: | :----------- | :----------- |
|  `local` | (local machine) | (local machine) | (local machine) |
|  `aspnet-perf-lin` | INTEL, 12 logical cores, 1 socket, 32GB | Ubuntu 20.04, Kernel 5.4.0 | Intel(R) Xeon(R) E-2336 CPU @ 2.90GHz |
|  `aspnet-perf-win` | INTEL, 12 logical cores, 1 socket, 32GB | Windows Server 2022 | Intel(R) Xeon(R) E-2336 CPU @ 2.90GHz |
|  `aspnet-citrine-lin` | INTEL, logical 28 cores, 1 socket, 32GB | Ubuntu 20.04, Kernel 5.4.0 | Intel(R) Xeon(R) Gold 5120 CPU @ 2.20GHz |
|  `aspnet-citrine-win` | INTEL, logical 28 cores, 1 socket, 32GB | Windows Server 2022 | Intel(R) Xeon(R) Gold 5120 CPU @ 2.20GHz |
|  `aspnet-citrine-amd` | AMD, logical 48 cores, 1 socket, 64GB, 1 NUMA | Ubuntu 20.04, Kernel 5.4.0 | AMD EPYC 7402P 24-Core Processor |
|  `aspnet-citrine-amd2` | AMD, logical 48 cores, 1 socket, 64GB, 4 NUMA | Ubuntu 20.04, Kernel 5.4.0 | AMD EPYC 7402P 24-Core Processor |
|  `aspnet-citrine-arm-lin` | ARM64, 80 physical cores, 1 socket, 1 NUMA, 128 GB | Ubuntu 20.04, Kernel 5.4.0 | Neoverse-N1 |
|  `aspnet-gold-lin` | INTEL, 56 physical cores, 1 socket, 1 NUMA, 64 GB | Ubuntu 22.04, Kernel 5.15.0 | Intel(R) Xeon(R) Gold 6330 CPU @ 2.00GHz |
|  `aspnet-gold-win` | INTEL, 56 physical cores, 1 socket, 1 NUMA, 64 GB | Ubuntu 22.04, Kernel 5.15.0 | Intel(R) Xeon(R) Gold 6330 CPU @ 2.00GHz |

For testing purpose only, the __local__ profile requires a local agent to run:

```
dotnet tool install Microsoft.Crank.Agent --version "0.2.0-*" --global

crank-agent
```

## Plaintext benchmarks

The source code for these benchmarks is located [here](https://github.com/aspnet/benchmarks/tree/main/src/Benchmarks).
These scenarios return a "Hello World" string and the client uses HTTP pipelining with 16 requests.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/plaintext.benchmarks.yml --scenario plaintext --profile aspnet-perf-lin
```

### Available scenarios

- `plaintext`: Middleware implementation 
- `https`: Middleware implementation, using HTTPS
- `endpoint`: Middleware implementation with Endpoint routing
- `mvc`: Controller implementation
- `mapaction`: Endpoint routing implementation
- `connectionclose`: Middleware implementation, the connection is closed after each request. Pipelining is disabled.
- `connectionclosehttps`: Same as `connectionclose` but over https

## Json benchmarks

The source code for these benchmarks is located [here](https://github.com/aspnet/benchmarks/tree/main/src/Benchmarks).
These scenarios serialize and return a `{ "message": "Hello World" }` string.

The serialization is done with `System.Text.Json`.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/json.benchmarks.yml --scenario json --profile aspnet-perf-lin
```

### Available scenarios

- `json`: Middleware implementation 
- `https`: Middleware implementation, using HTTPS
- `mvc`: Controller implementation
- `mapaction`: Endpoint routing implementation

## Database benchmarks

The source code for these benchmarks is located [here](https://github.com/aspnet/benchmarks/tree/main/src/Benchmarks).
These scenarios execute some database requests and return either HTML or Json.

The database server is PostgresQL.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/database.benchmarks.yml --scenario fortunes --profile aspnet-perf-lin
```

### Available scenarios

The following scenarios are implemented from a middleware (no MVC)

- `fortunes`
- `fortunes_ef`
- `fortunes_dapper`
- `single_query`
- `single_query_ef`
- `single_query_dapper`
- `multiple_queries`
- `multiple_queries_ef`
- `multiple_queries_dapper`
- `updates`
- `updates_ef`
- `updates_dapper`

The following scenarios are using ASP.NET CORE MVC

- `fortunes_ef_mvc_https`

The suffixes represent different database access strategies:
 
- No suffix: Raw ADO.NET 
- "ef" suffix: Entity Framework Core
- "dapper" suffix: Dapper

## Platform benchmarks

The source code for these benchmarks is located [here](https://github.com/aspnet/benchmarks/tree/main/src/BenchmarksApps/Kestrel).
These scenarios are highly optimized to provide the best performance, in detriment of extensibility and code complexity.

The database server is PostgresQL.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/platform.benchmarks.yml --scenario fortunes --profile aspnet-perf-lin
```

### Available scenarios

- `plaintext`
- `json`
- `fortunes`
- `fortunes_ef`
- `fortunes_dapper`
- `single_query`
- `multiple_queries`
- `updates`
- `caching`

## Proxy benchmarks

These scenarios are running several web proxies, including [YARP](https://github.com/microsoft/reverse-proxy).

The downstream service returns a variable size content. By default the result is 10 bytes. 

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/proxy.benchmarks.yml --scenario proxy-httpclient --profile aspnet-perf-lin
```

### Available scenarios

- `proxy-yarp`
- `proxy-httpclient`
- `proxy-nginx`
- `proxy-haproxy`
- `proxy-envoy`

### Custom arguments

#### Response size

The size of the payload can be changed by adapting the path of the requested url:

```
--variable path=/?s=100
```

#### Protocols

The server and downstream protocols can be changed to http (default), https and h2.
The following example shows how to use "h2 - https":

```
--variable serverScheme=https --variable serverProtocol=http2 --variable downstreamScheme=https --variable downstreamProtocol=https
```

> NOTES: nginx doesn't support http2 on the upstream server c.f. https://www.nginx.com/blog/http2-module-nginx/#QandA

#### Body size
Custom bodies can be used with the `bodyFile` and `verb` variables like this:

```
--variable bodyFile=https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/assets/100B.txt --variable verb=POST
```

Local and remote files can be used.

#### Headers

As for many other scenarios, the existing web load clients (wrk, bombardier, ...) are configured to support predefined headers:

- `none` (default)
- `plaintext`: `"Accept: text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7"`
- `json`: `"Accept: application/json,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7"`
- `html`: `"Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"`

The variable `presetHeaders` is used to select one of these:

```
--variable presetHeaders=plaintext
```

## Frameworks benchmarks

These scenarios measure the performance of different other frameworks

- NodeJs (JavaScript)
- Actix (Rust)
- FastHttp (Go)
- Netty (Java)
- Drogon (C++)
- Wizzardo (Java)
- Gin (Go)
- Express (JavaScript)

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/te.benchmarks.yml --scenario plaintext_nodejs --profile aspnet-perf-lin
```

### Available scenarios

- `plaintext_nodejs`, `json_nodejs`, `fortunes_nodejs`
- `plaintext_actix`, `json_actix`, `fortunes_actix`
- `plaintext_fasthttp`, `json_fasthttp`, `fortunes_fasthttp`
- `plaintext_netty`, `json_netty`
- `plaintext_drogon`, `json_drogon`, `fortunes_drogon`
- `plaintext_wizzardo`, `json_wizzardo`, `single_query_wizzardo`, `multiple_queries_wizzardo`, `updates_wizzardo`, `cached_queries_wizzardo`
- `plaintext_gin`, `json_gin`, `fortunes_gin`
- `plaintext_express`, `json_express`, `fortunes_express`

## Grpc benchmarks

These scenarios measure the performance of different Grpc  server and clients implementations.

- Go
- Native (C) 
- ASP.NET

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/grpc.benchmarks.yml --scenario grpcaspnetcoreserver-grpcnetclient --profile aspnet-perf-lin --variable streams=70 --variable connections=1
```

### Available scenarios

- `grpcaspnetcoreserver-grpcnetclient`
- `grpccoreserver-grpcnetclient`
- `grpcgoserver-grpcnetclient`
- `grpcaspnetcoreserver-grpccoreclient`
- `grpccoreserver-grpccoreclient`
- `grpcgoserver-grpccoreclient`
- `grpcaspnetcoreserver-grpcgoclient`
- `grpccoreserver-grpcgoclient`
- `grpcgoserver-grpcgoclient`
- `grpcaspnetcoreserver-h2loadclient`
- `grpccoreserver-h2loadclient`
- `grpcgoserver-h2loadclient`

#### Arguments

- Number of streams: 
  - `--variable streams=1`
  - `--variable streams=70`
- Number of connections: 
  - `--variable connections=1`
  - `--variable connections=28`
- Protocol: 
  - `--variable protocol=h2`
  - `--variable protocol=h3`
  - `--variable protocol=h2c`
- Call types:
  - Unary: `--variable scenario=unary`
  - Server streaming: `--variable scenario=serverstreaming`
  - Ping ping streaming: `--variable scenario=pingpongstreaming`

## Static file benchmarks

Middleware based application that serve static files of any size.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/staticfiles.benchmarks.yml --scenario static --profile aspnet-perf-lin
```

### Available scenarios

- `static`

The filename and size can be changed by adapting these variables:

```
--variable sizeInBytes=1024
--variable filename=file.txt
```

This scenario can easily reach the max network bandwidth. To verify that use `--load.options.displayOutput true` which will display the __wrk__ output, including the transfer rate. Example of saturated network for a 40Gb/s NIC (Citrine).

```
[load] Transfer/sec:      4.37GB
```

## SignalR benchmarks

These scenarios are running various SignalR benchmarks. The transport and serialization methods can be configured.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/signalr.benchmarks.yml --scenario signalr --profile aspnet-perf-lin --variable scenario=echo --variable transport=websockets --variable protocol=messagepack
```

### Available scenarios

- `signalr`

#### Arguments

- Scenario: 
  - `--variable scenario=broadcast`
  - `--variable scenario=echo`
  - `--variable scenario=echoAll`
- Transport: 
  - `--variable transport=websockets`
  - `--variable transport=serversentevents`
  - `--variable transport=longpolling`
- Protocol: 
  - `--variable protocol=json`
  - `--variable protocol=messagepack`

> Note: MessagePack is not supported with ServerSentEvents

## WebSockets benchmarks

These scenarios are running various WebSockets benchmarks.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/websocket.benchmarks.yml --scenario websocket --profile aspnet-perf-lin --variable scenario=echo
```

### Available scenarios

- `websocket`

#### Arguments

- Scenario:
  - `--variable scenario=echo`

## Orchard benchmarks

These scenarios are running various Orchard Core CMS benchmarks with either Sqlite or PostgresQL.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/orchard.benchmarks.yml --scenario about-sqlite --profile aspnet-perf-win
```

### Available scenarios

- `about-sqlite`: Simple about page using Sqlite
- `about-postgresql`: Simple about page using PostgresQL

## Distributed cache benchmarks

These scenarios are running various distributed cache benchmarks.

For all the scenarios, the store is initialized with `CacheCount` cache entries. Each request will issue a read or a write based on the `WriteRatio` 
argument choosing a key randomly. The HTTP response won't contain the cache entry data so that it doesn't impact the raw store perf measurement.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/redis.benchmarks.yml --scenario redis --profile aspnet-perf-lin
```

### Available scenarios

- `redis`: Read/Write cache entries using StackExchangeRedisCache distributed cache provider.
- `redis_local`: same as `redis`, with the Redis server on the same machine as the application.
- `memory`: Read/Write cache entries using MemoryCache distributed cache provider.
- `nullcache`: Uses a fake implementation of `IDistributedCache` that does nothing when invoked.

#### Arguments

- cacheCount (256): The number or cache entries created in the store.
  - `--variable cacheCount=256`
- keyLength (16): The size or the keys. They are generated using random alphanumeric characters.
  - `--variable keyLength=16`
- contentLength (64): The size of the cache entries.
  - `--variable contentLength=64`
- writeRatio (0): The ratio between reads and writes. Set to 100 for only writes.
  - `--variable writeRatio=0`

## Native AOT benchmarks

These scenarios provide benchmarks to help improve the Native AOT performance.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/goldilocks.benchmarks.yml --scenario basicminimalapipublishaot --profile aspnet-perf-lin
```
### Available scenarios

- `basicminimalapivanilla`
- `basicminimalapipublishaot`
- `basicgrpcvanilla`
- `basicgrpcpublishaot`

## HTTP client benchmarks

These scenarios are running various HTTP client benchmarks.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/httpclient.benchmarks.yml --scenario httpclient-kestrel-get --profile aspnet-perf-lin --variable concurrencyPerHttpClient=200
```

### Available scenarios

- `httpclient-kestrel-get`: Execute GET requests to the server. Client is HttpClient, server is Kestrel (minimal API).
- `httpclient-kestrel-post`: Execute POST requests to the server. Client is HttpClient, server is Kestrel (minimal API).
- `httpclient-kestrel-post-slow`: Execute POST requests to the server, upload content 1 byte at a time. Similar to `httpclient-kestrel-post`, with pre-defined parameters (`requestContentWriteSize=1`, `requestContentFlushAfterWrite=true`, `requestContentUnknownLength=true`)
- `wrk-kestrel-get`: Execute GET requests to the server. Client is WRK, server is Kestrel (minimal API).

#### Arguments

- Server port to use:
  - `--variable serverPort=<N>` (default: `5000`)
- HTTP Version to use:
  - `--variable httpVersion="1.1"` (default)
  - `--variable httpVersion="2.0"`
  - `--variable httpVersion="3.0"` -- *requires HTTPS*
- Whether to use HTTPS:
  - `--variable useHttps=false` (default)
  - `--variable useHttps=true`
- Pre-defined request headers:
  - `--variable requestHeaders=none` (default)
  - `--variable requestHeaders=connectionclose`
  - `--variable requestHeaders=expectcontinue`
- Number of generated request headers with static values:
  - `--variable generatedStaticRequestHeadersCount=<N>` (default: `0`)
- Number of generated request headers with values changing per each request:
  - `--variable generatedDynamicRequestHeadersCount=<N>` (default: `0`)
- Response content size in bytes:
  - `--variable responseSize=<N>` (default: `0`, meaning no content)
- Server's HTTP/3 stream limit:
  - `--variable http3StreamLimit=<N>` (default: `0`, meaning server's default. Max value is 65535)
- Number of HTTP clients (HttpClient in `httpClient` job, connection in WRK):
  - `--variable numberOfHttpClients=<N>` (default: `1`)
- Number of concurrect requests per one HTTP client -- *unsupported by WRK*:
  - `--variable concurrencyPerHttpClient=<N>` (default: `1`)
- Whether to collect request timings (time to headers, to first content byte, to last content byte) -- *unsupported by WRK*:
  - `--variable collectRequestTimings=false` (default)
  - `--variable collectRequestTimings=true`
- Duration of the warmup in seconds:
  - `--variable warmup=<N>` (default: `15`)
- Duration of the test in seconds:
  - `--variable duration=<N>` (default: `15`)

*POST-specific arguments:*

- Request content size in bytes:
  - `--variable requestContentSize=<N>` (default: `1024` for POST scenarios, `0` for GET scenarios)
- How many bytes (at max) will be written per each write of request content (also chunk size if chunked encoding is used):
  - `--variable requestContentWriteSize=<N>` (default: `81920`)
- Whether to flush request content stream after each write:
  - `--variable requestContentFlushAfterWrite=false` (default)
  - `--variable requestContentFlushAfterWrite=true`
- Whether request content length should be unknown to server. If `false`, will send `Content-Length` header, if `true`, will use chunked encoding for HTTP/1.1 or unknown content length for HTTP/2.0 and 3.0:
  - `--variable requestContentUnknownLength=false` (default)
  - `--variable requestContentUnknownLength=true`

*HttpClient-specific arguments:*

- Max number of HTTP/1.1 connections per server:
  - `--variable http11MaxConnectionsPerServer=<N>` (default: `0`, meaning no limit)
- Enable multiple HTTP/2.0 connections:
  - `--variable http20EnableMultipleConnections=true` (default)
  - `--variable http20EnableMultipleConnections=false`
- Whether to use WinHttpHandler instead of SocketsHttpHandler:
  - `--variable useWinHttpHandler=false` (default)
  - `--variable useWinHttpHandler=true` -- *requires Windows*
- Whether to use HttpMessageInvoker instead of HttpClient:
  - `--variable useHttpMessageInvoker=false` (default)
  - `--variable useHttpMessageInvoker=true`
- Whether to use HttpClient.DefaultRequestHeaders for pre-set and static headers:
  - `--variable useDefaultRequestHeaders=false` (default)
  - `--variable useDefaultRequestHeaders=true`

## Blazor benchmarks

These scenarios provide benchmarks for Blazor.

### Sample

```
crank --config https://raw.githubusercontent.com/dotnet/performance/main/src/benchmarks/gc/scenarios/CrankConfiguration.yaml --scenario ssr --profile aspnet-perf-lin
```

## GC benchmarks

These scenarios provide benchmarks to help improve the performance of the .NET GC.
### Sample

```
crank --config https://raw.githubusercontent.com/dotnet/performance/main/src/benchmarks/gc/scenarios/CrankConfiguration.yaml --scenario 2gb-pinning --profile aspnet-citrine-win --application.framework net8.0 
```
### Available scenarios

- `0gb - Server`
- `2gb - Server`
- `2gb-pinning - Server`
- `20gb - Server`
- `0gb - Workstation`
- `2gb - Workstation`
- `2gb-pinning - Workstation`
- `20gb - Workstation`

## Micro benchmarks

These scenarios are running [dotnet micro benchmarks](https://github.com/dotnet/performance) from the https://github.com/dotnet/performance repository.

### Sample

```
crank --config https://github.com/aspnet/benchmarks/blob/main/scenarios/dotnet.benchmarks.yml?raw=true --scenario linq --profile aspnet-perf-win
```

### Available scenarios

- `linq`
- `sockets`

The scenario named `custom` can be used to pass any custom filter variable like so:

```
crank --config https://github.com/aspnet/benchmarks/blob/main/scenarios/dotnet.benchmarks.yml?raw=true --scenario custom --profile aspnet-perf-win --variable filter=*LinqBenchmarks*
```

## FAQ

> The following command lines assume that the job to configure is named `application` which should be the name used in most of the configuration defined in this document.

### How to use the latest .NET version?

By default the pre-configured scenarios are using the latest official release of .NET. The `framework` argument of a service can change which TFM to target.

Example:

Using the daily builds of .NET by targeting net8.0 for the `application` service.

```
--application.framework net8.0
```

### How to benchmark a custom fork and/or branch?

If you can create a local branch to this repository, then the argument to add is:

```
--application.source.branchOrCommit mybranch
```

If your branch is in another fork then you also need to change the `repository` value:

```
--application.source.respository https://github.com/myorg/myrepo
```

### How to upload custom files?

Local files or complete folders can be sent to the running agents to path runtime and aspnet core files:

```
--application.options.outputFiles c:\build\System.Private.CoreLib.dll
```

or for a folder:

```
--application.options.outputFiles c:\build\kestrel
```

This argument can be repeated in case there are multiple sources.

### Running with specific runtime versions to isolate regressions

The list of public builds for ASP.NET and Core CLR are available on these feeds:
- ASP.NET: https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet8/nuget/v3/flat2/Microsoft.AspNetCore.App.Runtime.linux-x64/index.json
- Core CLR: https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet8/nuget/v3/flat2/Microsoft.NetCore.App.Runtime.linux-x64/index.json

Use `--application.runtimeVersion x.y.z` and `--application.aspnetCoreVersion x.y.z` to isolate which build, and ultimately which commit introduced a regression.

### Getting the command line from Power BI

Most pages in the Power BI dashboard list the crank command lines that were used. Select a benchmark and an environment to filter the table:

![image](https://user-images.githubusercontent.com/1165805/168184269-70732746-8490-4e6e-abe8-cf161ea421d6.png)
