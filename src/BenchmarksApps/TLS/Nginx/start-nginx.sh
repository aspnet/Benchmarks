#!/bin/bash
# Log the application start to stdout
echo "$(date) - Application started."

# Start Nginx
nginx -g "daemon off;"