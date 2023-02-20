FROM golang:alpine AS build-env
WORKDIR /src
COPY . ./
RUN go build -o todo

FROM alpine
ENV protocol h2c
WORKDIR /app
COPY --from=build-env /src/todo /app/
COPY --from=build-env /src/certs /app/certs
EXPOSE 5000
ENTRYPOINT ./todo -protocol=$protocol
