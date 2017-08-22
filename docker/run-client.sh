#!/usr/bin/env bash

#echo on
set -x

docker run \
    -it \
    --rm \
    --mount type=bind,source=/mnt,target=/tmp \
    --network host \
    benchmarks \
    /root/.dotnet/dotnet \
    /benchmarks/src/BenchmarksClient/bin/Debug/netcoreapp2.0/BenchmarksClient.dll \
    | tee /tmp/benchmarks-client.log
