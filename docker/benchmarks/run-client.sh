#!/usr/bin/env bash

#echo on
set -x

docker run \
    -d \
    --name benchmarks-client \
    --network host \
    --restart always \
    benchmarks \
    /root/.dotnet/dotnet \
    /benchmarks/src/BenchmarksClient/bin/Debug/netcoreapp2.0/BenchmarksClient.dll \
    | tee /tmp/benchmarks-client.log
