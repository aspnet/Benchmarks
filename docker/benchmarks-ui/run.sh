#!/usr/bin/env bash

#echo on
set -x

# "--network host" - Better performance than the default "bridge" driver
docker run \
    -d \
    -it \
    --init \
    --name benchmarks-ui \
    --network host \
    --restart always \
    benchmarks-ui \
    --urls http://*:6001
