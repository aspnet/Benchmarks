#!/usr/bin/env bash

#echo on
set -x

# "--network host" - Better performance than the default "bridge" driver
# TODO: the local mount (/mnt/prcheck) should be a name or positional parameter
docker run \
    -d \
    -it \
    --init \
    --mount type=bind,source=/mnt/prcheck,target=/jobs \
    --name jenkins \
    --network host \
    --restart always \
    jenkins
