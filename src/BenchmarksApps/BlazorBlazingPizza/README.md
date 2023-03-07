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
