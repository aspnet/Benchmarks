FROM golang:alpine AS build-env
RUN apk --no-cache add build-base git bzr mercurial gcc
WORKDIR /src
COPY . ./
RUN go build -o benchmarks

FROM alpine
ENV SERVER_ADDR host.docker.internal:5000
ENV PROTOCOL h2c
ENV SCENARIO unary
ENV CONNECTIONS 1
ENV STREAMS 1
ENV DURATION 10
ENV WARMUP 5
ENV REQUEST_SIZE 0
ENV RESPONSE_SIZE 0
WORKDIR /app
COPY --from=build-env /src/benchmarks /app/
COPY --from=build-env /src/certs /app/certs
EXPOSE 5000
ENTRYPOINT ./benchmarks --server_addr=$SERVER_ADDR --scenario=$SCENARIO --protocol=$PROTOCOL --connections=$CONNECTIONS --streams=$STREAMS \
    --warmup=$WARMUP --duration=$DURATION --request_size=$REQUEST_SIZE --response_size=$RESPONSE_SIZE
