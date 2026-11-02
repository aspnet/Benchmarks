#!/usr/bin/env bash

NAME="${1:-}"

if [ -z "$NAME" ]
then
    echo "Service name is missing. Usage: ./run.sh <servicename> [docker options]" >&2
    exit 1
fi
shift

required_settings=(
    SERVICE_BUS_CONNECTION_STRING SERVICE_BUS_QUEUE
    SQL_CONNECTION_STRING SQL_SERVER_CLIENTID SQL_SERVER_TENANTID SQL_SERVER_CERT_PATH
)
certificate_paths=(SQL_SERVER_CERT_PATH)
auth_docker_args=()
worker_args=()

if [[ -n "${SERVICE_BUS_MI_CLIENT_ID:-}" ]]
then
    client_id_pattern='^[[:xdigit:]]{8}-[[:xdigit:]]{4}-[[:xdigit:]]{4}-[[:xdigit:]]{4}-[[:xdigit:]]{12}$'
    if [[ ! "$SERVICE_BUS_MI_CLIENT_ID" =~ $client_id_pattern ]]
    then
        echo "SERVICE_BUS_MI_CLIENT_ID must be a user-assigned managed identity client ID (GUID)" >&2
        exit 1
    fi

    auth_docker_args=(--entrypoint crank-azdo --env SERVICE_BUS_MI_CLIENT_ID)
    worker_args=(
        --connection-string SERVICE_BUS_CONNECTION_STRING --queue SERVICE_BUS_QUEUE
        --mi-client-id SERVICE_BUS_MI_CLIENT_ID
    )
else
    required_settings+=(SERVICE_BUS_CLIENTID SERVICE_BUS_TENANTID SERVICE_BUS_CERT_PATH)
    certificate_paths+=(SERVICE_BUS_CERT_PATH)
    auth_docker_args=(
        -v "${SERVICE_BUS_CERT_PATH:-}:/certs/servicebus.pfx"
        --env SERVICE_BUS_CLIENTID --env SERVICE_BUS_TENANTID
    )
fi

for setting in "${required_settings[@]}"
do
    if [[ "${!setting:-}" != *[![:space:]]* ]]
    then
        echo "$setting needs to be set" >&2
        exit 1
    fi
done

if [[ -n "${SERVICE_BUS_MI_CLIENT_ID:-}" ]]
then
    namespace_pattern='^[[:alnum:]]([[:alnum:]-]*[[:alnum:]])?(\.[[:alnum:]]([[:alnum:]-]*[[:alnum:]])?)+$'
    if [[ ! "$SERVICE_BUS_CONNECTION_STRING" =~ $namespace_pattern ]]
    then
        echo "SERVICE_BUS_CONNECTION_STRING must be a fully qualified namespace hostname for managed identity, not a connection string or URI" >&2
        exit 1
    fi
fi

for setting in "${certificate_paths[@]}"
do
    if [[ ! -f "${!setting}" || ! -r "${!setting}" ]]
    then
        echo "$setting must point to a readable certificate file" >&2
        exit 1
    fi
done

docker run \
    -d \
    -it \
    --init \
    --name "$NAME" \
    --network host \
    --restart always \
    --env SERVICE_BUS_QUEUE \
    --env SERVICE_BUS_CONNECTION_STRING \
    "${auth_docker_args[@]}" \
    --env SQL_CONNECTION_STRING \
    --env SQL_SERVER_CLIENTID \
    --env SQL_SERVER_TENANTID \
    -v "${SQL_SERVER_CERT_PATH}:/certs/sqlserver.pfx" \
    --env SQL_SERVER_CERT_PATH=/certs/sqlserver.pfx \
    "$@" \
    azdocontroller "${worker_args[@]}"
