#!/usr/bin/env bash

#echo on
set -x

server_ip=$(ip route get 1 | awk '{print $NF;exit}')

docker run \
    -it \
    --rm \
    --mount type=bind,source=/mnt,target=/tmp \
    --network host \
    benchmarks \
    /root/.dotnet/dotnet \
    /Benchmarks/src/BenchmarksServer/bin/Debug/netcoreapp2.0/BenchmarksServer.dll \
    -n $server_ip
