#!/usr/bin/env bash

#echo on
set -x

server_ip=$(ip route get 1 | awk '{print $NF;exit}')

if [ -e /var/log/waagent.log ]
then
    hardware=Cloud
else
    hardware=Physical
fi

if [[ -v DBHOST ]]
then
    POSTGRES_CONN = 'Server=$DBHOST;Database=hello_world;User Id=benchmarkdbuser;Password=benchmarkdbpass;Maximum Pool Size=1024;NoResetOnClose=true'
    MYSQL_CONN = 'Server=$DBHOST;Database=hello_world;User Id=benchmarkdbuser;Password=benchmarkdbpass'
    MSSQL_CONN = 'Server=$DBHOST;Database=hello_world;User Id=sa;Password=Benchmarkdbp@55'

# "--network host" - Better performance than the default "bridge" driver
# "-v /var/run/docker.sock" - Give container access to the host docker daemon 
    docker run \
        -d \
        --log-opt max-size=10m \
        --log-opt max-file=3 \
        --mount type=bind,source=/mnt,target=/tmp \
        --name benchmarks-server \
        --network host \
        --restart always \
        -v /var/run/docker.sock:/var/run/docker.sock \
        -e POSTGRES_CONN \
        -e MYSQL_CONN \
        -e MSSQL_CONN \
        benchmarks \
        bash -c \
        "/root/.dotnet/dotnet \
        /benchmarks/src/BenchmarksServer/bin/Debug/netcoreapp2.0/BenchmarksServer.dll \
        -n $server_ip \
        --hardware $hardware \
        $@"
else
    echo DBHOST needs to be defined
fi