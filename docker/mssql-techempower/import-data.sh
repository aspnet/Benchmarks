#!/usr/bin/env bash
set -euo pipefail

sqlcmd_path="/opt/mssql-tools18/bin/sqlcmd"
sqlcmd=("$sqlcmd_path" -S localhost -U sa -P "$SA_PASSWORD" -C)

if [ ! -x "$sqlcmd_path" ]; then
  echo "sqlcmd not found at expected path '$sqlcmd_path'" >&2
  exit 1
fi

max_attempts=60
attempt=1
while [ "$attempt" -le "$max_attempts" ]; do
  if "${sqlcmd[@]}" -l 1 -Q "SELECT 1" > /dev/null 2>&1; then
    break
  fi

  if [ "$attempt" -eq "$max_attempts" ]; then
    echo "Timed out waiting for SQL Server to accept connections after ${max_attempts} attempts." >&2
    exit 1
  fi

  sleep 1
  attempt=$((attempt + 1))
done

"${sqlcmd[@]}" -b -d master -i create.sql
