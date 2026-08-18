#!/usr/bin/env bash
set -euo pipefail

/opt/mssql/bin/sqlservr &
sqlservr_pid=$!

cleanup() {
  kill "$sqlservr_pid" > /dev/null 2>&1 || true
}

trap cleanup EXIT
./import-data.sh
trap - EXIT

wait "$sqlservr_pid"