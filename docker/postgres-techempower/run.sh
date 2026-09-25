#!/usr/bin/env bash

#echo on
set -x

docker run \
    -d \
    --log-opt max-size=10m \
    --log-opt max-file=3 \
    --name postgres-techempower \
    -p 5432:5432 \
    --restart always \
    postgres-techempower
