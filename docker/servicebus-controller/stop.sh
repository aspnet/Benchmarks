#!/usr/bin/env bash

#echo on
set -x

NAME="$1"
shift

if [ -z "$NAME" ]
then
    echo "Service name is missing. Usage: ./stop.sh <servicename>"
    exit 1
fi

docker stop "$NAME"
docker rm "$NAME"
