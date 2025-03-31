#!/bin/bash
# Replace the placeholder in the nginx.conf.template with the actual value of SSL_LOGGING_ENABLED
if [ "$SSL_LOGGING_ENABLED" = "true" ]; then
    sed 's|{{SSL_LOGGING_ENABLED}}|if=$SSL_LOGGING_ENABLED|g' /etc/nginx/nginx.conf.template > /etc/nginx/nginx.conf
else
    sed 's|{{SSL_LOGGING_ENABLED}}||g' /etc/nginx/nginx.conf.template > /etc/nginx/nginx.conf
fi

echo "$(date) - Application started. Logging: $LOGGING"
nginx -g "daemon off;" # start nginx