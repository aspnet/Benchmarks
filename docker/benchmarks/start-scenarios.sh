#!/usr/bin/env bash

#echo on
set -x

while [ $# -ne 0 ]
do
    name="$1"
    case "$name" in
        --sql)
            shift
            sql="$1"
            ;;
        -s|--server)
            shift
            server="$1"
            ;;
        -c|--client)
            shift
            client="$1"
            ;;
        *)
            say_err "Unknown argument \`$name\`"
            exit 1
            ;;
    esac

    shift
done

if [ -z "$sql" ]
then
    echo "--sql needs to be set"
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

docker run \
    -d \
    --log-opt max-size=10m \
    --log-opt max-file=3 \
    --mount type=bind,source=/mnt,target=/tmp \
    --name benchmarks-scenarios \
    --network host \
    --restart always \
    benchmarks \
    bash -c \
    "dotnet msbuild -c Release ./build/repo.proj \
    /p:BENCHMARK_SERVER=\"$server\" \
    /p:BENCHMARK_CLIENT=\"$client\"  \
    /p:BENCHMARK_SQL=\"$sql\"  \
    | tee /tmp/scenarios-\$(date '+%Y-%m-%dT%H-%M').log "
    
