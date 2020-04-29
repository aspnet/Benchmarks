#!/usr/bin/env bash

#echo on
set -x

# Deployment
# - Mount the File Share to /mnt/prcheck. The instructions are available in the Azure Portal on 
# the File Share account to use, with the link Connect.
# Execute this script with "-s SERVER_URL -c CLIENT_URL"

# "--network host" - Better performance than the default "bridge" driver
# TODO: the local mount (/mnt/prcheck) should be a name or positional parameter
docker run \
    -d \
    -it \
    --init \
    --mount type=bind,source=/mnt/prcheck,target=/jobs \
    --name jobconsumer \
    --network host \
    --restart always \
    jobconsumer \
    --jobs-path /jobs "$@"
