#!/usr/bin/env bash

url="http://*:5001"
name="crank-agent"
others=""
dockerargs=""

while [ $# -ne 0 ]
do
    case "$1" in
        --url)
            shift
            url="$1"
            shift
            ;;
        --name)
            shift
            name="$1"
            shift
            ;;
        *)
            others+=" $1"
            shift
            ;;
    esac
done

if [ -n "$CRANK_AGENT_AZURE_RELAY_CERT_CLIENT_ID" ]
then
    dockerargs+=" --env CRANK_AGENT_AZURE_RELAY_CERT_CLIENT_ID"
fi

if [ -n "$CRANK_AGENT_AZURE_RELAY_CERT_TENANT_ID" ]
then
    dockerargs+=" --env CRANK_AGENT_AZURE_RELAY_CERT_TENANT_ID"
fi

if [ -n "$CRANK_AGENT_AZURE_RELAY_CERT_PATH" ]
then
    dockerargs+=" -v $CRANK_AGENT_AZURE_RELAY_CERT_PATH:/certs/relay.pfx --env CRANK_AGENT_AZURE_RELAY_CERT_PATH=/certs/relay.pfx"
fi

# cgroupfs is mapped to allow docker to create cgroups without permissions issues (cgroup v2)
# set cgroupns to host to allow the container to share the host's cgroup namespace (matches v1 and v2 namespace modes: https://docs.docker.com/engine/containers/runmetrics/#running-docker-on-cgroup-v2)
# docker.sock is mapped to be able to manage other docker instances from this one
docker run -it --name $name -d --network host --restart always \
    --log-opt max-size=1G --privileged \
    --cgroupns=host \
    -v /sys/fs/cgroup/:/sys/fs/cgroup/ \
    -v /var/run/docker.sock:/var/run/docker.sock $dockerargs \
    crank-agent \
    --url $url $others
