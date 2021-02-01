# http, https
ARG SERVER_SCHEME=http

# http, http2
ARG SERVER_PROTOCOL=http

# http, https
ARG DOWNSTREAM_SCHEME=http

# http, http2
ARG DOWNSTREAM_PROTOCOL=http

FROM envoyproxy/envoy-dev:latest AS base

ENV DOWNSTREAM_ADDRESS tfb-database
ENV DOWNSTREAM_PORT 5000

ADD envoy.yaml /etc/envoy.yaml
ADD testCert.crt /etc/testCert.crt
ADD testCert.rsa /etc/testCert.key

ADD run.sh /
RUN chmod +x /run.sh

# Listening to http connections proxying http
FROM base AS scheme-http-http-to-http-http
# ARG SERVER_SCHEME 
ADD envoy-http.yaml /etc/envoy.yaml

# Listening to https connections proxying http
FROM base AS scheme-https-http-to-http-http
# ARG SERVER_SCHEME
ADD envoy-https-http.yaml /etc/envoy.yaml

# Listening to https connections proxying https
FROM base AS scheme-https-http-to-https-http
# ARG SERVER_SCHEME
ADD envoy-https-https.yaml /etc/envoy.yaml

# Listening to h2 connections proxying h2
FROM base AS scheme-https-http2-to-https-http2
# ARG SERVER_SCHEME
ADD envoy-http2.yaml /etc/envoy.yaml

# Listening to h2 connections proxying http 1.1
FROM base AS scheme-https-http2-to-http-http
# ARG SERVER_SCHEME
ADD envoy-https-http.yaml /etc/envoy.yaml

FROM scheme-${SERVER_SCHEME}-${SERVER_PROTOCOL}-to-${DOWNSTREAM_SCHEME}-${DOWNSTREAM_PROTOCOL} AS final

ENTRYPOINT ["/run.sh"]
