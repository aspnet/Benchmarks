FROM golang:alpine AS build-env
RUN apk --no-cache add build-base git bzr mercurial gcc
WORKDIR /src
COPY . ./
RUN go build -o benchmarks

FROM alpine
ENV protocol h2c
WORKDIR /app
COPY --from=build-env /src/benchmarks /app/
COPY --from=build-env /src/certs /app/certs
EXPOSE 5000
ENTRYPOINT ./benchmarks -protocol=$protocol
