#!/usr/bin/env bash

#echo on
set -x

docker stop benchmarks-server
docker rm benchmarks-server
