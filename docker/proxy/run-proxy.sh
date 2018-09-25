#!/usr/bin/env bash

# Configure which server to proxy to    
#   --proxy-url http://server:port/path 
# Configure which port to listen to
#   --url http:*:PORT

docker run -d -it --network host --name benchmarks-proxy --restart always benchmarks-proxy "$@"
