#!/usr/bin/env bash

#echo on
set -x

docker build -t benchmarks -f Dockerfile ../../
