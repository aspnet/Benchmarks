FROM mcr.microsoft.com/dotnet/nightly/sdk:latest AS build
WORKDIR /app
COPY . .
RUN dotnet publish src/Benchmarks/Benchmarks.csproj -c Release -o out -f net7.0 /p:BenchmarksTargetFramework=net7.0 /p:MicrosoftAspNetCoreAppPackageVersion=$ASPNET_VERSION

FROM composite.azurecr.io/aspnet-composite:7.0 AS runtime
# ENV ASPNETCORE_URLS http://*:5000
WORKDIR /app
COPY --from=build /app/out ./

ENTRYPOINT ["dotnet", "Benchmarks.dll"]