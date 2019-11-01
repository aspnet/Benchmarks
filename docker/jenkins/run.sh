#!/usr/bin/env bash

#echo on
set -x

docker run \
    --log-opt max-size=10m \
    --log-opt max-file=3 \
    -d --name jenkins \
    --restart always -p 80:8080 -p 50000:50000 \
    -v jenkins_home:/var/jenkins_home -e JAVA_OPTS="-Dmail.smtp.starttls.enable=true" \
    benchmarks-jenkins
