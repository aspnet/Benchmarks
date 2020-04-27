#!/usr/bin/env bash

#echo on
set -x

docker stop servicebus-controller
docker rm servicebus-controller
