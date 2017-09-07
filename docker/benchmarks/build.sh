#!/usr/bin/env bash

#echo on
set -x

if docker info | grep aufs; then
   echo "aufs storage driver detected.  Build may fail with 'bad interpreter: Text file busy'.  Recommend changing storage driver to overlay2."
else
    docker build -t benchmarks -f Dockerfile ../../
fi
