#!/usr/bin/env bash

docker run -it --rm -v $PWD:/traces benchmarks-driver "$@"
