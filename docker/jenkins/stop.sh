#!/usr/bin/env bash

#echo on
set -x

docker stop benchmarks-jenkins
docker rm benchmarks-jenkins
