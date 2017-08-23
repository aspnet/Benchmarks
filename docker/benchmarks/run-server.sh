#!/usr/bin/env bash

#echo on
set -x

server_ip=$(ip route get 1 | awk '{print $NF;exit}')

docker run \
    -d \
    --mount type=bind,source=/mnt,target=/tmp \
    --name benchmarks-server \
    --network host \
    --restart always \
    benchmarks \
    /root/.dotnet/dotnet \
    /benchmarks/src/BenchmarksServer/bin/Debug/netcoreapp2.0/BenchmarksServer.dll \
    -n $server_ip \
    | tee /tmp/benchmarks-server.log
