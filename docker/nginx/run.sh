#!/usr/bin/env bash
#echo on
set -x

sed -i "s|WORKER_PROCESSES|$WORKER_PROCESSES|g" /etc/nginx/nginx.conf
sed -i "s|PROXY_PASS|$PROXY_PASS|g" /etc/nginx/nginx.conf
cat /etc/nginx/nginx.conf
nginx -g "daemon off;"
