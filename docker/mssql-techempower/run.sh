#!/usr/bin/env bash

#echo on
set -x

docker run \
    -d \
    --log-opt max-size=10m \
    --log-opt max-file=3 \
    --name mysql-techempower \
    --network host \
    --restart always \
    -e SA_PASSWORD=$MSSQL_PASSWORD \
    mssql-techempower
