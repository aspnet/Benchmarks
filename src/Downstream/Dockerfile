FROM mcr.microsoft.com/dotnet/core/sdk:6.0 AS build
WORKDIR /app

# copy csproj and restore as distinct layers
COPY *.csproj .
RUN dotnet restore

# copy everything else and build app
COPY . .
RUN dotnet publish -c release -o out

FROM mcr.microsoft.com/dotnet/core/sdk:6.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./
ENTRYPOINT ["dotnet", "Downstream.dll"]
