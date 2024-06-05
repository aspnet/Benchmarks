#!/usr/bin/env bash

#echo on
set -x

NAME="$1"
shift

if [ -z "$NAME" ]
then
    echo "Service name is missing. Usage: ./run.sh <servicename> [args]"
    exit 1
fi

if [ -z "SERVICE_BUS_CONNECTION_STRING" ]
then
    echo "SERVICE_BUS_CONNECTION_STRING needs to be set"
    exit 1
fi

if [ -z "SERVICE_BUS_QUEUE" ]
then
    echo "SERVICE_BUS_QUEUE needs to be set"
    exit 1
fi

if [ -z "SERVICE_BUS_CERT_PATH" ]
then
    echo "SERVICE_BUS_CERT_PATH needs to be set"
    exit 1
fi

if [ -z "SQL_CONNECTION_STRING" ]
then
    echo "SQL_CONNECTION_STRING needs to be set"
    exit 1
fi

if [ -z "SERVICE_BUS_CLIENTID" ]
then
    echo "SERVICE_BUS_CLIENTID needs to be set"
    exit 1
fi

if [ -z "SERVICE_BUS_TENANTID" ]
then
    echo "SERVICE_BUS_TENANTID needs to be set"
    exit 1
fi

if [ -z "SQL_SERVER_CLIENTID" ]
then
    echo "SQL_SERVER_CLIENTID needs to be set"
    exit 1
fi

if [ -z "SQL_SERVER_TENANTID" ]
then
    echo "SQL_SERVER_TENANTID needs to be set"
    exit 1
fi

if [ -z "SQL_SERVER_CERT_PATH" ]
then
    echo "SQL_SERVER_CERT_PATH needs to be set"
    exit 1
fi

# The SQL_SERVER_CERT_PATH is used to bind a volume to the location of the certificate in 
# ths host machine. Then it's converted to the location in the container such taht the
# crank commande line which is sent in the message can still use the same ENV name but with the internal value

docker run \
    -d \
    -it \
    --init \
    --name "$NAME" \
    --network host \
    --restart always \
    --env SERVICE_BUS_QUEUE \
    -v "$SERVICE_BUS_CERT_PATH":/certs/servicebus.pfx \
    --env SERVICE_BUS_CONNECTION_STRING \
    --env SERVICE_BUS_CLIENTID \
    --env SERVICE_BUS_TENANTID \
    --env SQL_CONNECTION_STRING \
    --env SQL_SERVER_CLIENTID \
    --env SQL_SERVER_TENANTID \
    -v "$SQL_SERVER_CERT_PATH":/certs/sqlserver.pfx \
    --env SQL_SERVER_CERT_PATH=/certs/sqlserver.pfx \
    "$@" \
    azdocontroller
