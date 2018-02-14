#!/usr/bin/env bash

#echo on
set -x

while [ $# -ne 0 ]
do
    name="$1"
    case "$name" in
        -sip|-server-ip)
            shift
            server_ip="$1"
            ;;
        -v|--hardware-version)
            shift
            hardware_version="$1"
            ;;
        -h|--hardware)
            shift
            hardware="$1"
            ;;
        -d|--db)
            shift
            DBHOST="$1"
            ;;
        -url|--url)
            shift
            url="$1"
            ;;
        -n|--name)
            shift
            dockername="$1"
            ;;
        *)
            say_err "Unknown argument \`$name\`"
            exit 1
            ;;
    esac

    shift
done


if [ -z "$server_ip" ]
then
    # tries to get the ip from the available NICs, but it's recommended to set it manually to use the fastest one
    server_ip=$(ip route get 1 | awk '{print $NF;exit}')

    echo "Using server_ip=$server_ip"
fi

if [ -z "$url" ]
then
    url="http://*:5001"
fi

if [ -z "$dockername" ]
then
    dockername="benchmarks-server"
fi

if [ -z "$hardware_version" ]
then
    echo "hardware_version needs to be set"
    exit 1
fi

if [ -e /var/log/waagent.log ]
then
    hardware=Cloud
else
    hardware=Physical
fi

if [[ -v DBHOST ]]
then
    postgresql="--postgresql \"Server=$DBHOST;Database=hello_world;User Id=benchmarkdbuser;Password=benchmarkdbpass;Maximum Pool Size=1024;NoResetOnClose=true\""
    mysql="--mysql \"Server=$DBHOST;Database=hello_world;User Id=benchmarkdbuser;Password=benchmarkdbpass;Maximum Pool Size=1024;\""
    mssql="--mssql \"Server=$DBHOST;Database=hello_world;User Id=sa;Password=Benchmarkdbp@55\""
    mongodb="--mongodb \"mongodb://$DBHOST:27017?maxPoolSize=1024\""

    # "--network host" - Better performance than the default "bridge" driver
    # "-v /var/run/docker.sock" - Give container access to the host docker daemon 
    docker run \
        -d \
        --log-opt max-size=10m \
        --log-opt max-file=3 \
        --mount type=bind,source=/mnt,target=/tmp \
        --name $dockername \
        --network host \
        --restart always \
        -v /var/run/docker.sock:/var/run/docker.sock \
        benchmarks \
        bash -c \
        "dotnet run -c Debug --project src/BenchmarksServer/BenchmarksServer.csproj \
        -n $server_ip \
        --url $url \
        --hardware $hardware \
        --hardware-version $hardware_version \
        $postgresql  \
        $mysql  \
        $mssql \
        $mongodb \
        $@"
else
    echo DBHOST needs to be defined
fi
