FROM envoyproxy/envoy-dev:latest

ENV DOWNSTREAM_ADDRESS tfb-database
ENV DOWNSTREAM_PORT 5000
ENV SERVER_ADDRESS 0.0.0.0

ADD envoy.yaml /etc/envoy.yaml

ADD run.sh /
RUN chmod +x /run.sh

ENTRYPOINT ["/run.sh"]
