#!/usr/bin/env bash

#echo on
set -x

while [ $# -ne 0 ]
do
    name="$1"
    case "$name" in
        -p|--password)
            shift
            password="$1"
            ;;
        -n|--name)
            shift
            dockername="$1"
            ;;
        -s|--server)
            shift
            server="$1"
            ;;
        -c|--client)
            shift
            client="$1"
            ;;
        -d|--description)
            shift
            description="$1"
            ;;
        -t|--timespan)
            shift
            timespan="$1"
            ;;
        --port)
            shift
            port="$1"
            ;;
        --duration)
            shift
            duration="$1"
            ;;
        *)
            say_err "Unknown argument \`$name\`"
            exit 1
            ;;
    esac

    shift
done

if [ -z "$dockername" ]
then
    echo "--name needs to be set"
    exit 1
fi

if [ -z "$password" ]
then
    echo "--password needs to be set"
    exit 1
fi

if [ -z "$server" ]
then
    echo "--server needs to be set"
    exit 1
fi

if [ -z "$client" ]
then
    echo "--client needs to be set"
    exit 1
fi

if [ -z "$description" ]
then
    echo "--description needs to be set"
    exit 1
fi

if [ -z "$port" ]
then
    port=5000
fi

if [ -z "$duration" ]
then
    duration=15
fi

if [ -z "$timespan" ]
then
    echo "--timespan needs to be set"
    exit 1
fi

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
        "dotnet run -c Debug --project src/BenchmarksDriver/BenchmarksDriver.csproj \
        --server \"$server\" \
        --client \"$client\" \
        --port \"$port\" \
        --duration \"$duration\" \
        --jobs src/Benchmarks/benchmarks.html.json \
        -n MvcDbFortunesEf \
        --webHost KestrelLibuv \
        -q 'Server=aspnetbenchmarks.database.windows.net;Database=AspNetBenchmarks;User Id=aspnet;Password=$password' \
        --table AspNetStress \
        --database PostgreSql \
        --clientThreads 2 \
        --connections 2 \
        --session `date '+%Y-%m-%dT%H-%M'` \
        --description \"$description\" \
        --span \"$timespan\"  \
        -e \"ASPNETCORE_LogLevel=Error\""
