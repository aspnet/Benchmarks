FROM microsoft/dotnet:sdk AS build-env
WORKDIR /benchmarks

# copy everything
COPY . ./

#build
RUN dotnet publish -c Release --project /benchmarks/src/BenchmarksDriver/BenchmarksDriver.csproj -o /benchmarks/src/BenchmarksDriver/published
