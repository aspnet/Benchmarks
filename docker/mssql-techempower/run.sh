#!/usr/bin/env bash

#echo on
set -x

docker run \
    -d \
    --log-opt max-size=10m \
    --log-opt max-file=3 \
    --name mssql-techempower \
    --network host \
    --restart always \
    -e ACCEPT_EULA=Y \
    -e MSSQL_PID=Enterprise \
    -e SA_PASSWORD=Benchmarkdbp@55 \
    mssql-techempower
