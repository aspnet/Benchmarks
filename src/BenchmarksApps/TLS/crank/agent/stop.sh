#!/usr/bin/env bash

NAME="$1"
shift

if [ -z "$NAME" ]
then
    NAME="crank-agent"
fi

docker stop "$NAME"
docker rm "$NAME"
