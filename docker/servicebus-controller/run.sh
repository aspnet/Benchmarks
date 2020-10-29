#!/usr/bin/env bash

#echo on
set -x

NAME="$1"
shift

if [ -z "$NAME" ]
then
    echo "Service name is missing. Usage: ./run.sh <servicename> [args]"
    exit 1
fi

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
    --name "$NAME" \
    --network host \
    --restart always \
    --env SERVICE_BUS_CONNECTION_STRING \
    --env SERVICE_BUS_QUEUE \
    --env SQL_CONNECTION_STRING \
    azdocontroller \
    "$@"
