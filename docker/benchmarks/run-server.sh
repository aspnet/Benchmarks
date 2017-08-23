#!/usr/bin/env bash

#echo on
set -x

server_ip=$(ip route get 1 | awk '{print $NF;exit}')

docker run \
    -d \
    --log-opt max-size=10m \
    --log-opt max-file=3 \
    --mount type=bind,source=/mnt,target=/tmp \
    --name benchmarks-server \
    --network host \
    --restart always \
    benchmarks \
    /root/.dotnet/dotnet \
    /benchmarks/src/BenchmarksServer/bin/Debug/netcoreapp2.0/BenchmarksServer.dll \
    -n $server_ip
