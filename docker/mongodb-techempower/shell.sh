#!/usr/bin/env bash

#echo on
set -x

docker exec -it mongodb-techempower mongo --host mongodb:27017 
