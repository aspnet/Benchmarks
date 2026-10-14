FROM mcr.microsoft.com/dotnet/nightly/sdk:11.0 AS build
WORKDIR /source
COPY Directory.Build.props NuGet.Config ./
COPY src/TodoApi/TodoApi.csproj src/TodoApi/packages.lock.json src/TodoApi/
RUN dotnet restore src/TodoApi/TodoApi.csproj --locked-mode
COPY src/TodoApi/ src/TodoApi/
RUN dotnet publish src/TodoApi/TodoApi.csproj --configuration Release --no-restore --self-contained false --output /app

FROM mcr.microsoft.com/dotnet/nightly/aspnet:11.0 AS final
WORKDIR /app
ENV PORT=8080
EXPOSE 8080
COPY --from=build /app ./
USER $APP_UID
ENTRYPOINT ["dotnet", "TodoApi.dll"]
