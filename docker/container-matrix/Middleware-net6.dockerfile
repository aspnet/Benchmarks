FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish src/Benchmarks/Benchmarks.csproj -c Release -o out -f net6.0

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS runtime
# ENV ASPNETCORE_URLS http://*:5000
WORKDIR /app
COPY --from=build /app/out ./

ENTRYPOINT ["dotnet", "Benchmarks.dll"]
