#!/usr/bin/env bash

#echo on
set -x

docker run \
    -d \
    --name postgres-techempower \
    --network host \
    --restart always \
    postgres-techempower
