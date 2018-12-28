#!/usr/bin/env bash

#echo on
set -x

# "--network host" - Better performance than the default "bridge" driver
docker run \
    -d \
    -it \
    --init \
    --name benchmarks-downstream \
    --network host \
    --rm \
    --restart always \
    benchmarks-downstream \
    --urls http://*:5001
