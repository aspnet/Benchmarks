#!/usr/bin/env bash

#echo on
set -x

docker stop benchmarks-downstream
docker rm benchmarks-downstream
