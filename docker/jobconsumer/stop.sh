#!/usr/bin/env bash

#echo on
set -x

docker stop jobconsumer
docker rm jobconsumer
