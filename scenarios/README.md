# Benchmark Scenarios

This folder contains benchmarks that represent common scenarios to look at for the .NET team.

## Requirements

These jobs can be executed using the .NET Crank global tool. 
[.NET Core 3.1](<http://dot.net>) is required to install the global tool.

Install `crank` with the following command:

```
dotnet tool install Microsoft.Crank.Controller --version "0.1.0-*" --global
```

Alternatively, update `crank` with the following command:

```
dotnet tool update Microsoft.Crank.Controller --version "0.1.0-*" --global
```

## Profiles

Each profile defines a set of machines, private IPs and ports that are used to run a benchmark.

| Profile       | Arch     | OS     |
| :------------- | :----------: | :----------- |
|  `aspnet-perf-lin` | INTEL, 12 cores | Ubuntu 18.04, Kernel 4.x |
|  `aspnet-perf-win` | INTEL, 12 cores | Windows Server 2016 |
|  `aspnet-citrine-lin` | INTEL, 28 cores | Ubuntu 18.04, Kernel 4.x |
|  `aspnet-citrine-win` | INTEL, 28 cores | Windows Server 2016 |
|  `aspnet-citrine-arm` | ARM64, 32 cores | Ubuntu 18.04, Kernel 4.x |
|  `aspnet-citrine-amd` | AMD, 48 cores | Ubuntu 18.04, Kernel 4.x |

## Plaintext benchmarks

The source code for these benchmarks is located [here](https://github.com/aspnet/Benchmarks/tree/master/src/Benchmarks).
These scenarios return a "Hello World" string and the client uses HTTP pipelining with 16 requests.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/master/scenarios/plaintext.benchmarks.yml --scenario plaintext --profile aspnet-perf-lin
```

### Available scenarios

- `plaintext`: Middleware implementation 
- `https`: Middleware implementation, using HTTPS
- `endpoint`: Middleware implementation with Endpoint routing
- `mvc`: Controller implementation
- `connectionclose`: Middleware implementation, the connection is closed after each request. Pipelining is disabled.

## Json benchmarks

The source code for these benchmarks is located [here](https://github.com/aspnet/Benchmarks/tree/master/src/Benchmarks).
These scenarios serialize and return a `{ "message": "Hello World" }` string.

The serialization is done with `System.Text.Json`.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/master/scenarios/json.benchmarks.yml --scenario json --profile aspnet-perf-lin
```

### Available scenarios

- `json`: Middleware implementation 
- `https`: Middleware implementation, using HTTPS
- `mvc`: Controller implementation

## Database benchmarks

The source code for these benchmarks is located [here](https://github.com/aspnet/Benchmarks/tree/master/src/Benchmarks).
These scenarios execute some database requests and return either HTML or Json.

The database server is PostgresQL.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/master/scenarios/database.benchmarks.yml --scenario fortunes --profile aspnet-perf-lin
```

### Available scenarios

- `fortunes`: Middleware implementation using raw ADO.NET.
- `fortunes_ef`: Middleware implementation, using EF Core.

## Platform benchmarks

The source code for these benchmarks is located [here](https://github.com/aspnet/Benchmarks/tree/master/src/BenchmarksApps/Kestrel).
These scenarios are highly optimized to provide the best performance, in detriment of extensibility and code complexity.

The database server is PostgresQL.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/master/scenarios/platform.benchmarks.yml --scenario fortunes --profile aspnet-perf-lin
```

### Available scenarios

- `plaintext`
- `json`
- `fortunes`
- `single_query`
- `multiple_queries`
- `updates`

## Proxy benchmarks

These scenarios are running several web proxies, including [YARP](https://github.com/microsoft/reverse-proxy).

The downstream service returns a variable size content. By default the result is 10 bytes. 

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/master/scenarios/proxy.benchmarks.yml --scenario proxy-httpclient --profile aspnet-perf-lin
```

### Available scenarios

- `proxy-yarp`
- `proxy-httpclient`
- `proxy-nginx`
- `proxy-haproxy`
- `proxy-envoy`
- `proxy-baseline`: This scenario doesn't go through a proxy

The size of the payload can be changed by adapting the path of the requested url:

```
--variable path=/?s=100
```

## Frameworks benchmarks

These scenarios measure the performance of different other frameworks

- NodeJs
- Actix (Rust)
- FastHttp (Go)
- Netty (Java)
- ULib (C++)

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/master/scenarios/te.benchmarks.yml --scenario nodejs_plaintext --profile aspnet-perf-lin
```

### Available scenarios

- `plaintext_nodejs`
- `json_nodejs`
- `fortunes_nodejs`
- `plaintext_actix`
- `json_actix`
- `fortunes_actix`
- `plaintext_fasthttp`
- `json_fasthttp`
- `fortunes_fasthttp`
- `plaintext_ulib`
- `json_ulib`
- `fortunes_ulib`
- `plaintext_netty`
- `json_netty`

## Grpc benchmarks

These scenarios measure the performance of different Grpc  server and clients implementations.

- Go
- Native (C) 
- ASP.NET

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/master/scenarios/grpc.benchmarks.yml --scenario grpcaspnetcoreserver-grpcnetclient --profile aspnet-perf-lin --variable streams=70 --variable connections=1
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
- Protocol: 
  - `--variable protocol=h2c`
- Call types:
  - Unary: `--variable scenario=unary`
  - Server streaming: `--variable scenario=serverstreaming`
  - Ping ping streaming: `--variable scenario=pingpongstreaming`

## Static file benchmarks

Middleware based application that serve static files of any size.

### Sample

```
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/master/scenarios/staticfiles.benchmarks.yml --scenario static --profile aspnet-perf-lin
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

## FAQ

> The following command lines assume that the job to configure is named `application` which should be the name used in most of the configuration defined in this document.

### How to use the latest .NET version?

```
--application.channel latest --application.framework netcoreapp5.0
```

### How to upload custom files?

```
--application.options.outputFiles c:\build\System.Private.CoreLib.dll
```
