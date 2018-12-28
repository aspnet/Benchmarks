#!/usr/bin/env bash

#echo on
set -x

docker run \
    -d \
    --log-opt max-size=10m \
    --log-opt max-file=3 \
    --name mysql-techempower \
    --network host \
    --rm \
    --restart always \
    mysql-techempower
