#!/usr/bin/expect -f

set timeout [expr $env(PGBENCH_DURATION) * 2]

spawn pgbench -h $env(PGBENCH_HOST) -U benchmarkdbuser -T $env(PGBENCH_DURATION) -n -M $env(PGBENCH_QUERYMODE) -f fortunes.sql -j $env(PGBENCH_THREADS) -c $env(PGBENCH_CONNECTIONS) hello_world
expect "Password:" {send "benchmarkdbpass\r"}
interact
