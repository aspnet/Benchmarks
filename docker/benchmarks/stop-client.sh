#!/usr/bin/env bash

#echo on
set -x

docker stop benchmarks-client
docker rm benchmarks-client
