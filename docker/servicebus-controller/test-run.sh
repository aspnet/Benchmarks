#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
test_dir="$(mktemp -d)"
mkdir "$test_dir/bin" "$test_dir/certs with spaces"
servicebus_cert="$test_dir/certs with spaces/servicebus [test];\$.pfx"
sql_cert="$test_dir/certs with spaces/sql [test];\$.pfx"
: > "$servicebus_cert"
: > "$sql_cert"

cleanup()
{
    rm -f -- "$test_dir/bin/docker" "$test_dir/args" "$test_dir/mi-client-id" \
        "$test_dir/output" "$servicebus_cert" "$sql_cert"
    rmdir -- "$test_dir/bin" "$test_dir/certs with spaces" "$test_dir"
}
trap cleanup EXIT

cat > "$test_dir/bin/docker" <<'EOF'
#!/usr/bin/env bash
printf '%s\0' "$@" >> "$DOCKER_ARGS_FILE"
printf '%s' "${SERVICE_BUS_MI_CLIENT_ID:-}" > "$DOCKER_MI_FILE"
exit "${DOCKER_EXIT_CODE:-0}"
EOF
chmod +x "$test_dir/bin/docker"

fail()
{
    echo "FAIL: $*" >&2
    exit 1
}

run_test()
{
    (
        unset SERVICE_BUS_MI_CLIENT_ID AZURE_CLIENT_ID
        export PATH="$test_dir/bin:$PATH"
        export DOCKER_ARGS_FILE="$test_dir/args" DOCKER_MI_FILE="$test_dir/mi-client-id"
        export DOCKER_EXIT_CODE=0
        export SERVICE_BUS_CONNECTION_STRING="test-namespace.servicebus.windows.net"
        export SERVICE_BUS_QUEUE="test-queue"
        export SERVICE_BUS_CLIENTID="test-servicebus-client" SERVICE_BUS_TENANTID="test-servicebus-tenant"
        export SERVICE_BUS_CERT_PATH="$servicebus_cert"
        export SQL_CONNECTION_STRING="Server=test;Password=do-not-log-this;"
        export SQL_SERVER_CLIENTID="test-sql-client" SQL_SERVER_TENANTID="test-sql-tenant"
        export SQL_SERVER_CERT_PATH="$sql_cert"
        common_args=(
            run -d -it --init --name controller --network host --restart always
            --env SERVICE_BUS_QUEUE --env SERVICE_BUS_CONNECTION_STRING
        )
        sql_args=(
            --env SQL_CONNECTION_STRING --env SQL_SERVER_CLIENTID --env SQL_SERVER_TENANTID
            -v "$sql_cert:/certs/sqlserver.pfx" --env SQL_SERVER_CERT_PATH=/certs/sqlserver.pfx
        )
        "$@"
    )
    echo "PASS: $1"
}

launch()
{
    rm -f -- "$DOCKER_ARGS_FILE" "$DOCKER_MI_FILE"
    launch_status=0
    bash "$script_dir/run.sh" "$@" > "$test_dir/output" 2>&1 || launch_status=$?
    actual_args=()
    if [[ -f "$DOCKER_ARGS_FILE" ]]
    then
        while IFS= read -r -d '' arg
        do
            actual_args+=("$arg")
        done < "$DOCKER_ARGS_FILE"
    fi
    output="$(< "$test_dir/output")"
    [[ "$output" != *"do-not-log-this"* ]] || fail "launcher exposed a connection string or Docker option"
    [[ "$output" != *"$servicebus_cert"* && "$output" != *"$sql_cert"* ]] || fail "launcher exposed a certificate path"
}

assert_args()
{
    local index=0
    [[ ${#actual_args[@]} -eq $# ]] || fail "unexpected Docker argument count"
    for expected in "$@"
    do
        [[ "${actual_args[$index]}" == "$expected" ]] || fail "unexpected Docker argument at index $index"
        index=$((index + 1))
    done
}

assert_certificate_launch()
{
    assert_args "${common_args[@]}" \
        -v "$servicebus_cert:/certs/servicebus.pfx" --env SERVICE_BUS_CLIENTID --env SERVICE_BUS_TENANTID \
        "${sql_args[@]}" "$@" azdocontroller
}

assert_mi_launch()
{
    assert_args "${common_args[@]}" --entrypoint crank-azdo --env SERVICE_BUS_MI_CLIENT_ID \
        "${sql_args[@]}" "$@" azdocontroller \
        --connection-string SERVICE_BUS_CONNECTION_STRING --queue SERVICE_BUS_QUEUE \
        --mi-client-id SERVICE_BUS_MI_CLIENT_ID
    [[ "$(< "$DOCKER_MI_FILE")" == "$SERVICE_BUS_MI_CLIENT_ID" ]] || fail "selected identity was not exported to Docker"
}

assert_invalid()
{
    [[ $launch_status -eq 1 ]] || fail "invalid configuration did not exit with status 1"
    [[ ! -f "$DOCKER_ARGS_FILE" ]] || fail "invalid configuration invoked Docker"
    [[ "$output" == *"$1"* ]] || fail "missing diagnostic for $1"
}

certificate_default()
{
    launch controller
    [[ $launch_status -eq 0 ]] || fail "certificate launch failed"
    assert_certificate_launch
    [[ -z "$output" ]] || fail "launcher unexpectedly logged arguments"
}

empty_mi_keeps_certificates()
{
    export SERVICE_BUS_MI_CLIENT_ID=""
    certificate_default
}

azure_client_id_does_not_select_mi()
{
    export AZURE_CLIENT_ID="11111111-2222-3333-4444-555555555555"
    certificate_default
}

mi_without_servicebus_certificates()
{
    unset SERVICE_BUS_CLIENTID SERVICE_BUS_TENANTID SERVICE_BUS_CERT_PATH
    for identity in 11111111-2222-3333-4444-555555555555 AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE
    do
        export SERVICE_BUS_MI_CLIENT_ID="$identity"
        launch controller
        [[ $launch_status -eq 0 ]] || fail "MI launch failed"
        assert_mi_launch
        [[ -z "$output" ]] || fail "launcher unexpectedly logged arguments"
    done
}

mi_overrides_servicebus_certificates()
{
    export SERVICE_BUS_MI_CLIENT_ID="11111111-2222-3333-4444-555555555555"
    export SERVICE_BUS_CERT_PATH="$test_dir/nonexistent-servicebus.pfx"
    launch controller
    [[ $launch_status -eq 0 ]] || fail "MI incorrectly required a Service Bus certificate"
    assert_mi_launch
}

docker_options_remain_before_image()
{
    local extras=(--label 'description=spaces; $literal [value]' --env 'EXTRA_SECRET=do-not-log-this')
    launch controller "${extras[@]}"
    [[ $launch_status -eq 0 ]] || fail "certificate launch with Docker options failed"
    assert_certificate_launch "${extras[@]}"

    export SERVICE_BUS_MI_CLIENT_ID="11111111-2222-3333-4444-555555555555"
    launch controller "${extras[@]}"
    [[ $launch_status -eq 0 ]] || fail "MI launch with Docker options failed"
    assert_mi_launch "${extras[@]}"
}

missing_service_name()
{
    launch
    assert_invalid "Service name is missing"
    launch ""
    assert_invalid "Service name is missing"
}

required_settings()
{
    local mode setting value
    for mode in certificate mi
    do
        (
            settings=(SERVICE_BUS_CONNECTION_STRING SERVICE_BUS_QUEUE SQL_CONNECTION_STRING
                SQL_SERVER_CLIENTID SQL_SERVER_TENANTID SQL_SERVER_CERT_PATH)
            if [[ "$mode" == mi ]]
            then
                export SERVICE_BUS_MI_CLIENT_ID="11111111-2222-3333-4444-555555555555"
            else
                settings+=(SERVICE_BUS_CLIENTID SERVICE_BUS_TENANTID SERVICE_BUS_CERT_PATH)
            fi
            for setting in "${settings[@]}"
            do
                (
                    unset "$setting"
                    launch controller
                    assert_invalid "$setting"
                )
                for value in "" $' \t\n'
                do
                    (
                        export "$setting=$value"
                        launch controller
                        assert_invalid "$setting"
                    )
                done
            done
        )
    done
}

invalid_mi_never_uses_certificates()
{
    local identity
    for identity in "not-a-client-id" "   " "11111111-2222-3333-4444-555555555555 extra"
    do
        export SERVICE_BUS_MI_CLIENT_ID="$identity"
        launch controller
        assert_invalid SERVICE_BUS_MI_CLIENT_ID
    done
}

mi_requires_namespace_hostname()
{
    export SERVICE_BUS_MI_CLIENT_ID="11111111-2222-3333-4444-555555555555"
    local namespace
    for namespace in "Endpoint=sb://test/;SharedAccessKey=do-not-log-this" \
        "sb://test.servicebus.windows.net/" "https://test.servicebus.windows.net" \
        "test" "test namespace.servicebus.windows.net" "test..servicebus.windows.net"
    do
        export SERVICE_BUS_CONNECTION_STRING="$namespace"
        launch controller
        assert_invalid SERVICE_BUS_CONNECTION_STRING
    done

    export SERVICE_BUS_CONNECTION_STRING="test.servicebus.usgovcloudapi.net"
    launch controller
    [[ $launch_status -eq 0 ]] || fail "MI namespace validation assumed the public Azure cloud"
    assert_mi_launch
}

required_certificate_files()
{
    local mode setting path
    for mode in certificate mi
    do
        (
            settings=(SQL_SERVER_CERT_PATH)
            if [[ "$mode" == mi ]]
            then
                export SERVICE_BUS_MI_CLIENT_ID="11111111-2222-3333-4444-555555555555"
            else
                settings+=(SERVICE_BUS_CERT_PATH)
            fi
            for setting in "${settings[@]}"
            do
                for path in "$test_dir/nonexistent.pfx" "$test_dir"
                do
                    (
                        export "$setting=$path"
                        launch controller
                        assert_invalid "$setting"
                    )
                done
            done
        )
    done
}

unreadable_certificate_file()
{
    chmod a-r "$servicebus_cert"
    if [[ -r "$servicebus_cert" ]]
    then
        echo "SKIP: unreadable-file check (host still grants read access)"
    else
        launch controller
        assert_invalid SERVICE_BUS_CERT_PATH
    fi
    chmod u+r "$servicebus_cert"
}

docker_failure_does_not_retry()
{
    export DOCKER_EXIT_CODE=42
    launch controller
    [[ $launch_status -eq 42 ]] || fail "Docker's failure status was lost"
    assert_certificate_launch

    export SERVICE_BUS_MI_CLIENT_ID="11111111-2222-3333-4444-555555555555"
    launch controller
    [[ $launch_status -eq 42 ]] || fail "MI Docker failure fell back or lost its status"
    assert_mi_launch
}

run_test certificate_default
run_test empty_mi_keeps_certificates
run_test azure_client_id_does_not_select_mi
run_test mi_without_servicebus_certificates
run_test mi_overrides_servicebus_certificates
run_test docker_options_remain_before_image
run_test missing_service_name
run_test required_settings
run_test invalid_mi_never_uses_certificates
run_test mi_requires_namespace_hostname
run_test required_certificate_files
run_test unreadable_certificate_file
run_test docker_failure_does_not_retry
