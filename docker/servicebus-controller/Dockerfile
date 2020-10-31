FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-job

# Install dotnet tools
RUN dotnet tool install -g Microsoft.Crank.AzureDevOpsWorker --version "0.1.0-*"
RUN dotnet tool install -g Microsoft.Crank.Controller --version "0.1.0-*"

ENV PATH="${PATH}:/root/.dotnet/tools"

ENTRYPOINT ["crank-azdo", "--connection-string", "SERVICE_BUS_CONNECTION_STRING", "--queue", "SERVICE_BUS_QUEUE"]
