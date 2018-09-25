#!/usr/bin/env bash

#echo on
set -x

docker build --pull -t benchmarks-proxy -f Dockerfile ../../
