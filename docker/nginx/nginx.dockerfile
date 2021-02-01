# http, https
ARG SERVER_SCHEME=http

# http, http2
ARG SERVER_PROTOCOL=http

# NOTE: nginx doesn't support http2 connections to the upstream server

FROM nginx:latest AS base

ENV WORKER_PROCESSES auto
ENV DOWNSTREAM_SCHEME http
ENV DOWNSTREAM_ADDRESS tfb-database
ENV DOWNSTREAM_PORT 8080

ADD testCert.rsa /etc/ssl/private/testCert.rsa
ADD testCert.crt /etc/ssl/certs/testCert.crt

ADD run.sh /
RUN chmod +x /run.sh

# Listening to http connections
FROM base AS scheme-http-http
# ARG SERVER_SCHEME 
ADD nginx-http.conf /etc/nginx/nginx.conf

# Listening to https connections
FROM base AS scheme-https-http
# ARG SERVER_SCHEME
ADD nginx-https.conf /etc/nginx/nginx.conf

# Listening to h2 connections
FROM base AS scheme-https-http2
# ARG SERVER_SCHEME
ADD nginx-http2.conf /etc/nginx/nginx.conf

# Listening to h2c connections - not implemented, kept for reference
# FROM base AS scheme-http-http2
# ARG SERVER_SCHEME
# ADD nginx-http2.conf /etc/nginx/nginx.conf

FROM scheme-${SERVER_SCHEME}-${SERVER_PROTOCOL} AS final

ENTRYPOINT ["/run.sh"]
