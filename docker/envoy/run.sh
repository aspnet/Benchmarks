#!/usr/bin/env bash
#echo on
set -x

sed -i "s|DOWNSTREAM_ADDRESS|$DOWNSTREAM_ADDRESS|g" /etc/envoy.yaml
sed -i "s|DOWNSTREAM_PORT|$DOWNSTREAM_PORT|g" /etc/envoy.yaml

cat /etc/envoy.yaml

/usr/local/bin/envoy -c /etc/envoy.yaml