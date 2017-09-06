#!/usr/bin/env bash

#echo on
set -x

docker run \
    -it \
    --rm \
    benchmarks \
    /root/.dotnet/dotnet \
    /benchmarks/src/BenchmarksDriver/bin/Debug/netcoreapp2.0/BenchmarksDriver.dll \
    "$@"
