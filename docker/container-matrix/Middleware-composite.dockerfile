FROM mcr.microsoft.com/dotnet/nightly/sdk:11.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish src/Benchmarks/Benchmarks.csproj -c Release -o out -f net11.0 -p:BenchmarksTargetFramework=net11.0 -p:MicrosoftAspNetCoreAppPackageVersion=$ASPNET_VERSION

FROM mcr.microsoft.com/dotnet/nightly/aspnet:11.0-alpine-composite AS runtime
WORKDIR /app
COPY --from=build /app/out ./

ENTRYPOINT ["dotnet", "Benchmarks.dll"]
