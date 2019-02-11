FROM microsoft/dotnet:2.1-sdk-stretch AS build-ui
WORKDIR /app

# copy everything
COPY . ./

# build
WORKDIR /app/src/Benchmarks.UI.Server
RUN dotnet publish -c Release -o /app/out

FROM microsoft/dotnet:2.1-sdk-stretch AS build-driver
WORKDIR /app

# copy everything
COPY . ./

# build
WORKDIR /app/src/BenchmarksDriver
RUN dotnet publish -c Release -o /app/out

FROM microsoft/dotnet:2.1-aspnetcore-runtime-stretch-slim AS runtime
WORKDIR /app
COPY --from=build-ui /app/out ./
COPY --from=build-driver /app/out ./driver
ENTRYPOINT ["dotnet", "Benchmarks.UI.Server.dll", "--driverPath", "./driver"]
