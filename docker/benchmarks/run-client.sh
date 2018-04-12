#!/usr/bin/env bash

#echo on
set -x

docker run \
    -d \
    --log-opt max-size=10m \
    --log-opt max-file=3 \
    --name benchmarks-client \
    --network host \
    --restart always \
    benchmarks \
    dotnet run -c Release --project src/BenchmarksClient/BenchmarksClient.csproj
