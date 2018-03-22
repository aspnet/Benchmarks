FROM microsoft/dotnet:sdk AS build-env
WORKDIR /benchmarks

# copy everything
COPY . .
COPY ./docker/docker-continuous/scenario.sh ./docker/docker-continuous/scenario.sh 

#build
WORKDIR /benchmarks/src/BenchmarksDriver
RUN dotnet publish -c Release -o /benchmarks/src/BenchmarksDriver/published

WORKDIR /benchmarks
