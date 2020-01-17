FROM golang:alpine AS build-env
RUN apk --no-cache add build-base git bzr mercurial gcc
WORKDIR /src
COPY . ./
RUN go build -o benchmarks

FROM alpine 
WORKDIR /app
COPY --from=build-env /src/benchmarks /app/
EXPOSE 5000
ENTRYPOINT ./benchmarks