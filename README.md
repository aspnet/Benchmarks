# Benchmarks
Benchmarks for ASP.NET Core including (but not limited to) scenarios from the [TechEmpower Web Framework Benchmarks](https://www.techempower.com/benchmarks/).

The current results tracked by the ASP.NET team are available [at this location](https://aka.ms/aspnet/benchmarks).

# Setting up

## Components

The benchmarking infrastructure is made of these components:
- [Benchmarks](https://github.com/aspnet/benchmarks/tree/dev/src/Benchmarks), a web application that contains different scenarios to benchmark.
- [BenchmarksServer](https://github.com/aspnet/benchmarks/tree/dev/src/BenchmarksServer), a web application that queues jobs that are able to run custom web applications to be benchmarked.
- [BenchmarksClient](https://github.com/aspnet/benchmarks/tree/dev/src/BenchmarksClient), a web application that queues jobs that can create custom client loads on a web application.
- [BenchmarksDriver](https://github.com/aspnet/benchmarks/tree/dev/src/BenchmarksDriver), a command-line application that can enqueue server and client jobs and display the results locally.
- A database server that can run any or all of PostgreSql, Sql Server, MySql, MongoDb

## Setting up the infrastructure

This will assume you have Docker installed and are familiar with it.

### Setup the Benchmark Server

- Clone https://github.com/aspnet/benchmarks on the __dev__ branch
- Run `cd docker/benchmarks` 
- Run `./build.sh`, which will build a Docker image containing the benchmarking dependencies
- Add the following environment variables to the file `/etc/environment`
  - Set the environment variable `DBHOST=10.0.0.103` by replacing the IP with the one used to communicate with the database server
  - Set the environment variable `server_ip=10.0.0.102` by replacing the IP with the one used to communicate with the benchmark server
  - Set the environment variable `hardware_version=HPZ440` by replacing with a description of the hardware. This will be used when storing the results to distinguish environments
- Edit the file `/etc/hosts` and add `10.0.0.103 TFB-database` with the IP to the database server
- Run `./run-server.sh`

The application should start on port `5001`. Open a browser on this page and `OK` should be displayed.

### Setup the Benchmark Client

- Clone https://github.com/aspnet/benchmarks on the __dev__ branch
- Run `cd docker/benchmarks` 
- Run `./build.sh`, which will build a Docker image containing the benchmarking dependencies
- Then run `./run-client.sh`

The application should start on port `5002`. Open a browser on this page and `OK` should be displayed.

### Setup the Database Server

- Clone https://github.com/aspnet/benchmarks on the __dev__ branch

#### PostgreSql

- Run `cd docker/postgres-techempower` 
- Run `./build.sh`
- Then run `./run.sh`

This will create and run a Docker image containing PostgreSql and the Fortunes database that is used by Tech Empower scenarios.

### Run a job

On your computer,

- Clone https://github.com/aspnet/benchmarks on the __dev__ branch
- Run `cd src/BenchmarkDriver`
- Then run the following command after replacing the IP addresses to the ones you are using, 
```
dotnet run -c Debug `
 --server "http://10.0.0.102:5001" `
 --client "http://10.0.0.101:5002" `
 --jobs "../Benchmarks/benchmarks.Json.json" `
 -n Json
```

This will start the `Json` scenario using the `Benchmarks` application that is provided in https://github.com/aspnet/benchmarks/tree/dev/src/Benchmarks.
You can use another application by setting the correct arguments described on [this page](https://github.com/aspnet/benchmarks/blob/dev/src/BenchmarksDriver/README.md).

#### Selecting a database provider

Some of the `Benchmarks` application scenarios require the database to be selected from the driver.
By default no database driver will be configured and these scenarios will fail. For instance, running the 
`DbFortunesEF` scenario will require the driver to have the `--database` argument.

A good default is `--database PostgreSql` which will use NpgSql, but other providers are available. See the command line
arguments for the available ones.

#### Storing the results

The driver application can store the results of a job by passing a `-q [connectionstring]` argument. The connection
string must point to an existing SQL Server database. The first time it's called the required table will be created.
From there you can create reports using the tools of your choice.
