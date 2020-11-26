# Instructions to work with the MVC benchmarks

## Run benchmarks locally

```
crank --config .\benchmarks.crudapi.yml --scenario ApiCrudListProducts --profile local --application.source.localFolder $PWD\..\..\..
```

## Run on CI against your own branch

```
crank --config .\benchmarks.crudapi.yml --scenario ApiCrudListProducts --profile aspnet-perf-lin --application.source.branchOrCommit <<BranchOrCommit>>
```

## Override body files

```
crank --config .\benchmarks.crudapi.yml --scenario ApiCrudListProducts --profile local --application.source.localFolder $PWD\..\..\.. --profile local  --load.variables.bodyFile Modelbinding.BasicDTO.json --load.options.outputFiles C:\work\Benchmarks\src\BenchmarksApps\Mvc\ModelBinding\Modelbinding.BasicDTO.json
```

## Override custom headers on bombardier

```
  ModelBindingBasicDtoFromBody:
    application:
      job: mvcServer
    load:
      job: bombardier
      variables:
        verb: POST
        headers:
          jsonInput: '--header "content-type: application/json"'
        presetHeaders: 'jsonInput'
        path: /Modelbinding/BasicDTO/FromBody
        bodyFile: TODO.json
```

## Collect traces

```
crank --config .\benchmarks.crudapi.yml --scenario ApiCrudListProducts --profile aspnet-perf-lin --application.source.branchOrCommit <<BranchOrCommit>> --application.collect true
```

See https://github.com/dotnet/crank/blob/master/src/Microsoft.Crank.Controller/README.md for command-line details