#!/bin/bash

# Install dependencies
apt-get update \
    && apt-get install -y --no-install-recommends \
        cgroup-tools 

cgcreate -g cpu,cpuset:/cpugroup1 
cgcreate -g cpu,cpuset:/cpugroup2

cgset -r cpuset.cpus=0-5 /cpugroup1 
cgset -r cpuset.cpus=6-12 /cpugroup2

cgexec -g cpu:/cpugroup1 dotnet ./PlatformBenchmarks.dll &
cgexec -g cpu:/cpugroup2 dotnet ./PlatformBenchmarks.dll 

cgdelete -g cpu:/cpugroup1
cgdelete -g cpu:/cpugroup2
