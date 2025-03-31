FROM nginx:latest

# Define the ARG variable
ARG SSL_LOGGING_ENABLED=false

# Set the ENV variable based on the ARG variable
ENV SSL_LOGGING_ENABLED=${SSL_LOGGING_ENABLED}

# Copy configuration and SSL certificates
COPY config/nginx.conf /etc/nginx/nginx.conf
COPY config/cert.pem /etc/nginx/certs/cert.pem
COPY config/key.pem /etc/nginx/certs/key.pem
COPY start-nginx.sh /start-nginx.sh

# Make the script executable
RUN chmod +x /start-nginx.sh

# Expose port 8080 for HTTPS traffic
EXPOSE 8080

# Run the startup script
CMD ["/start-nginx.sh"]