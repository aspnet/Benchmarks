FROM microsoft/dotnet:sdk AS build-env
WORKDIR /benchmarks

# copy everything
COPY . ./

#build
WORKDIR /benchmarks/src/BenchmarksDriver
RUN dotnet publish -c Release -o /benchmarks/src/BenchmarksDriver/published
