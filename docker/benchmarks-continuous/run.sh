#!/usr/bin/env bash

docker run \
    -d \
    --log-opt max-size=10m \
    --log-opt max-file=3 \
    --mount type=bind,source=/mnt,target=/logs \
    --name benchmarks-scenarios \
    --network host \
    --restart always \
    benchmarks-scenarios \
    bash -c \
    "docker/benchmarks-continuous/scenarios.sh $@ | tee /logs/scenarios-\$(date '+%Y-%m-%dT%H-%M').log"

docker logs -f benchmarks-scenarios
