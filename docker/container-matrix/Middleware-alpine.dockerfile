FROM mcr.microsoft.com/dotnet/nightly/sdk:latest AS build
WORKDIR /app
COPY . .
RUN dotnet publish src/Benchmarks/Benchmarks.csproj -c Release -o out -f net9.0 -p:BenchmarksTargetFramework=net9.0 -p:MicrosoftAspNetCoreAppPackageVersion=$ASPNET_VERSION

FROM mcr.microsoft.com/dotnet/nightly/aspnet:9.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/out ./

ENTRYPOINT ["dotnet", "Benchmarks.dll" ]

