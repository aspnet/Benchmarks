FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-job

# Install dotnet tools
RUN dotnet tool install --framework net8.0 -g Microsoft.Crank.AzureDevOpsWorker --version "0.2.0-*"
RUN dotnet tool install --framework net8.0 -g Microsoft.Crank.Controller --version "0.2.0-alpha.25056.1"

ENV PATH="${PATH}:/root/.dotnet/tools"

ENTRYPOINT ["crank-azdo", "--connection-string", "SERVICE_BUS_CONNECTION_STRING", "--queue", "SERVICE_BUS_QUEUE", "--cert-client-id", "SERVICE_BUS_CLIENTID", "--cert-tenant-id", "SERVICE_BUS_TENANTID", "--cert-path", "/certs/servicebus.pfx", "--cert-sni"]
