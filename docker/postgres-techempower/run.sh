#!/usr/bin/env bash

#echo on
set -x

docker run \
    -it \
    --rm \
    --mount type=bind,source=/mnt,target=/tmp \
    --network host \
    postgres-techempower
