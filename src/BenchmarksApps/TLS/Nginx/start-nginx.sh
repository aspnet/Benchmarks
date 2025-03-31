#!/bin/bash
export SSL_LOGGING_ENABLED=${SSL_LOGGING_ENABLED}
echo "$(date) - Application started. Logging: $LOGGING"
nginx -g "daemon off;" # start nginx