#!/usr/bin/env bash

#echo on
set -x

docker stop benchmarks-scenarios
docker rm benchmarks-scenarios
