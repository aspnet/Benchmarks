FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish src/Benchmarks/Benchmarks.csproj -c Release -o out -f net8.0 /p:BenchmarksTargetFramework=net8.0 /p:MicrosoftAspNetCoreAppPackageVersion=$ASPNET_VERSION

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
# ENV ASPNETCORE_URLS http://*:5000
WORKDIR /app
COPY --from=build /app/out ./

ENTRYPOINT ["dotnet", "Benchmarks.dll"]
