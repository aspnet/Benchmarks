#!/usr/bin/env bash

#echo on
set -x

docker stop benchmarks-ui
docker rm benchmarks-ui
