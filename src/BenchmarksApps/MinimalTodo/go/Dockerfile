FROM golang:1.27-bookworm AS build
ENV GOTOOLCHAIN=local \
    GOPROXY=off \
    GOSUMDB=off
WORKDIR /src
COPY go.mod main.go server.go ./
RUN CGO_ENABLED=0 go build -trimpath -o /out/todo .

FROM ubuntu:26.04
RUN groupadd --gid 65532 nonroot && useradd --uid 65532 --gid 65532 --no-create-home --shell /usr/sbin/nologin nonroot
COPY --from=build --chown=65532:65532 /out/todo /todo
USER 65532:65532
ENV PORT=8080
EXPOSE 8080
STOPSIGNAL SIGTERM
ENTRYPOINT ["/todo"]
