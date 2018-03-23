#!/usr/bin/env bash

docker run \
    -d \
    --log-opt max-size=10m \
    --log-opt max-file=3 \
    --mount type=bind,source=/mnt,target=/logs \
    --network host \
    -e "BENCHMARKS_SERVER=$BENCHMARKS_SERVER" \
    -e "BENCHMARKS_CLIENT=$BENCHMARKS_CLIENT" \
    -e "BENCHMARKS_SQL=$BENCHMARKS_SQL" \
    -e "PLAINTEXT_LIBUV_THREAD_COUNT=$PLAINTEXT_LIBUV_THREAD_COUNT" \
    benchmarks-scenarios \
    bash -c \
    "docker/benchmarks-continuous/scenarios.sh | tee /logs/scenarios-\$(date '+%Y-%m-%dT%H-%M').log"
