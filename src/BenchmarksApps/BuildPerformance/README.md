### Build performance benchmarks

App that benchmarks performance of building ASP.NET Core apps using the CLI. Emphasis on inner loop where performance of changing a file is captured.

### How to run

crank --profile aspnet-perf-win --scenario buildperf_blazorwasmstandalone -c ./buildperformance.yml --application.selfContained false --application.variables.scenario <scenario-here>

Supported scenarios are listed in `Program.cs` and include `blazorwasm`, `blazorserver`, `mvc`, `api`, and `blazorwasm-hosted`.


