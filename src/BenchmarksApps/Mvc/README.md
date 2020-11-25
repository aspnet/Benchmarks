# Instructions to work with the MVC benchmarks

## Run benchmarks locally

crank --config .\benchmarks.crudapi.yml --scenario ApiCrudListProducts --profile local --application.source.localFolder $PWD\..\..\..

## Run on CI against your own branch

crank --config .\benchmarks.crudapi.yml --scenario ApiCrudListProducts --profile aspnet-perf-lin --application.source.branchOrCommit <<BranchOrCommit>>

## Collect traces

crank --config .\benchmarks.crudapi.yml --scenario ApiCrudListProducts --profile aspnet-perf-lin --application.source.branchOrCommit <<BranchOrCommit>> --application.collect true

See https://github.com/dotnet/crank/blob/master/src/Microsoft.Crank.Controller/README.md for command-line details