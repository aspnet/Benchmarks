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

if [ -z "SQL_CONNECTION_STRING" ]
then
    echo "SQL_CONNECTION_STRING needs to be set"
    exit 1
fi

docker run \
    -d \
    -it \
    --init \
    --name servicebus-controller \
    --network host \
    --restart always \
    --env SERVICE_BUS_CONNECTION_STRING=$SERVICE_BUS_CONNECTION_STRING \
    --env SERVICE_BUS_QUEUE=$SERVICE_BUS_QUEUE \
    --env SQL_CONNECTION_STRING=$SQL_CONNECTION_STRING \
    servicebus-controller \
    "$@"
