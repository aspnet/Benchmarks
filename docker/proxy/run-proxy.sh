#!/usr/bin/env bash

# --proxy-url http://server:port/path
docker run -d -it --network host --restart always "$@"
