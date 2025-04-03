FROM nginx:latest

# or 4096 key length
ARG CERT_KEY_LENGTH=2048 

# Copy configuration
COPY Nginx/config/nginx.conf /etc/nginx/nginx.conf
COPY Nginx/config/start-nginx.sh /start-nginx.sh

# Copy SSL certificates
COPY Certificates/${CERT_KEY_LENGTH}/cert.pem /etc/nginx/certs/cert.pem
COPY Certificates/${CERT_KEY_LENGTH}/key.pem /etc/nginx/certs/key.pem

# Make the script executable
RUN chmod +x /start-nginx.sh

# Expose port 8080 for HTTPS traffic
EXPOSE 8080

# Run the startup script
CMD ["/start-nginx.sh"]