#!/usr/bin/env bash

sed -i -e"s/^max_connections = 100.*$/max_connections = 1000/" /var/lib/postgresql/data/postgresql.conf
