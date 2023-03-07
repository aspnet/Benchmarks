# Blazor benchmarks

This sample app is currently for testing Blazor server-side rendering (SSR) performance only, i.e., prerendering a component on the server. It does not exercise interactive components - it is not using circuits or WebAssembly.

## To run the benchmark locally

1. Install Crank *and* Crank Agent as per instructions at https://github.com/dotnet/crank/blob/main/docs/getting_started.md
1. In a command prompt in any directory, start the agent by running `crank-agent`. Leave that running.
1. In a command prompt in this directory, submit the crank job:

    crank --config blazor.benchmarks.yml --scenario ssr --profile local

## To run the job in the ASP.NET Core perf infrastructure (works for .NET team only)

1. Install Crank as per instructions at https://github.com/dotnet/crank/blob/main/docs/getting_started.md (no need for the agent)
1. In a command prompt in this directory, submit the crank job. You can use `aspnet-perf-lin` or `aspnet-perf-win`, e.g.:

    crank --config blazor.benchmarks.yml --scenario ssr --profile aspnet-perf-lin

## To test the effects of local dotnet/aspnetcore repo source changes

If you're working on ASP.NET Core itself (i.e., in the `dotnet/aspnetcore` repo), you may want to observe the effects of your framework code changes on the app performance. To do so:

1. In the `dotnet/aspnetcore` repo, build `src\Components\benchmarkapps\BlazingPizza.Server` using `dotnet build -c Release`
2. In this repo, submit the job along with instructions to use your locally-built binaries:

    crank --config blazor.benchmarks.yml --scenario ssr --application.options.outputFiles c:\path\to\aspnetcore\artifacts\bin\BlazingPizza.Server\Release\net8.0\*.dll --profile aspnet-perf-lin
