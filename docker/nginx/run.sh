#!/usr/bin/env bash
#echo on
set -x

sed -i "s|WORKER_PROCESSES|$WORKER_PROCESSES|g" /etc/nginx/nginx.conf
sed -i "s|DOWNSTREAM_SCHEME|$DOWNSTREAM_SCHEME|g" /etc/nginx/nginx.conf
sed -i "s|DOWNSTREAM_ADDRESS|$DOWNSTREAM_ADDRESS|g" /etc/nginx/nginx.conf
sed -i "s|DOWNSTREAM_PORT|$DOWNSTREAM_PORT|g" /etc/nginx/nginx.conf
cat /etc/nginx/nginx.conf
nginx -g "daemon off;"
