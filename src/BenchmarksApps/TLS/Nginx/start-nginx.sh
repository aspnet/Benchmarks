#!/bin/bash
echo "$(date) - Application started. Logging: $LOGGING"
nginx -g "daemon off;" # start nginx