#!/usr/bin/env bash

#echo on
set -x

# See docker-entrypoint.sh for the available env values, which are not documented

docker run \
    -d \
    --log-opt max-size=10m \
    --log-opt max-file=3 \
    --name mongodb-techempower \
    --network host \
    --restart always \
    -e MONGO_INITDB_DATABASE benchmarks \
    mongodb-techempower
