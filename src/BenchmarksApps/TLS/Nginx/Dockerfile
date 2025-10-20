FROM nginx:latest

# or 4096 key length
ARG CERT_KEY_LENGTH=2048
ARG ENABLE_FIPS_MODE=false
ARG OPENSSL_CIPHER_STRING=TLS_AES_256_GCM_SHA384:TLS_AES_128_GCM_SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-SHA384:ECDHE-ECDSA-AES128-SHA256:ECDHE-RSA-AES256-SHA384:ECDHE-RSA-AES128-SHA256
ARG OPENSSL_GROUPS=P-384:P-256:P-521

# Copy configuration
COPY Nginx/config/nginx.conf /etc/nginx/nginx.conf
COPY Nginx/config/start-nginx.sh /start-nginx.sh

# Copy SSL certificates
COPY Certificates/${CERT_KEY_LENGTH}/cert.pem /etc/nginx/certs/cert.pem
COPY Certificates/${CERT_KEY_LENGTH}/key.pem /etc/nginx/certs/key.pem

# Configure OpenSSL for FIPS-compliant cipher suites if $ENABLE_FIPS_MODE
RUN if [ "$ENABLE_FIPS_MODE" = "true" ]; then \
        echo "=== FIPS MODE ENABLED - Configuring OpenSSL ===" && \
        cat /etc/ssl/openssl.cnf && \
        echo "" >> /etc/ssl/openssl.cnf && \
        echo "openssl_conf = openssl_init" >> /etc/ssl/openssl.cnf && \
        echo "[openssl_init]" >> /etc/ssl/openssl.cnf && \
        echo "ssl_conf = ssl_sect" >> /etc/ssl/openssl.cnf && \
        echo "[ssl_sect]" >> /etc/ssl/openssl.cnf && \
        echo "system_default = system_default_sect" >> /etc/ssl/openssl.cnf && \
        echo "[system_default_sect]" >> /etc/ssl/openssl.cnf && \
        echo "CipherString = $OPENSSL_CIPHER_STRING" >> /etc/ssl/openssl.cnf && \
        echo "Groups = $OPENSSL_GROUPS" >> /etc/ssl/openssl.cnf && \
        echo "=== FIPS Configuration Applied ===" && \
        tail -15 /etc/ssl/openssl.cnf; \
    else \
        echo "=== FIPS MODE DISABLED ==="; \
    fi

# Make the script executable
RUN chmod +x /start-nginx.sh

# Expose port 8080 for HTTPS traffic
EXPOSE 8080

# Run the startup script
CMD ["/start-nginx.sh"]