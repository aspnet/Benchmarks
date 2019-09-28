#!/usr/bin/env bash

#echo on
set -x

# "--network host" - Better performance than the default "bridge" driver
docker run \
    -d \
    -it \
    --init \
    --name jobconsumer \
    --network host \
    --restart always \
    jobconsumer \
    "$@"
