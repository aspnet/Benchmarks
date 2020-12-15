FROM haproxy:latest

ENV DOWNSTREAM_ADDRESS tfb-database
ENV DOWNSTREAM_PORT 5000

COPY haproxy.cfg /usr/local/etc/haproxy/haproxy.cfg
