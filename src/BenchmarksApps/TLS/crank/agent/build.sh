#!/usr/bin/env bash

cpuname=$(uname -p)
dockerfile="Dockerfile"
enable_fips="false"
cipher_string="TLS_AES_256_GCM_SHA384:TLS_AES_128_GCM_SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-SHA384:ECDHE-ECDSA-AES128-SHA256:ECDHE-RSA-AES256-SHA384:ECDHE-RSA-AES128-SHA256"
groups="P-384:P-256:P-521"

while [ $# -ne 0 ]
do
    case "$1" in
        --dockerfile)
            shift
            dockerfile="$1"
            shift
            ;;
        --enable-fips)
            enable_fips="true"
            shift
            ;;
        --cipher-string)
            shift
            cipher_string="$1"
            shift
            ;;
        --groups)
            shift
            groups="$1"
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--dockerfile <path>] [--enable-fips] [--cipher-string <string>] [--groups <groups>]"
            exit 1
            ;;
    esac
done

docker build -t crank-agent --build-arg CPUNAME=$cpuname --build-arg ENABLE_FIPS_MODE=$enable_fips --build-arg OPENSSL_CIPHER_STRING="$cipher_string" --build-arg OPENSSL_GROUPS="$groups" -f "$dockerfile" ../../