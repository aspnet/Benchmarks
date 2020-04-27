#!/usr/bin/env bash

#echo on
set -x

if [ -z "SERVICE_BUS_CONNECTION_STRING" ]
then
    echo "SERVICE_BUS_CONNECTION_STRING needs to be set"
    exit 1
fi

if [ -z "SERVICE_BUS_QUEUE" ]
then
    echo "SERVICE_BUS_QUEUE needs to be set"
    exit 1
fi

docker run \
    -d \
    -it \
    --init \
    --name servicebus-controller \
    --network host \
    --restart always \
    servicebus-controller \
    "$@"
