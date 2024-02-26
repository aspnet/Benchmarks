FROM mcr.microsoft.com/dotnet/nightly/sdk:latest

ARG INTERACTIVITY=None
ARG SERVER_SCHEME=http
ARG SERVER_ADDRESS=localhost
ARG SERVER_PORT=5000

RUN dotnet new blazor -int ${INTERACTIVITY} -n BlazorWebApp -o /src
RUN dotnet publish /src/BlazorWebApp/BlazorWebApp.csproj -c Release -o /publish
ENTRYPOINT /publish/BlazorWebApp --urls ${SERVER_SCHEME}://${SERVER_ADDRESS}:${SERVER_PORT}
