# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
USER root
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Define a build argument for the OpenSSL version
# lookup for openssl+branch version here https://pkgs.alpinelinux.org/packages?name=openssl&branch=v3.20&repo=&arch=x86_64
ARG OPENSSL_VERSION=1.1.1w-r1
ARG ALPINE_BRANCH=v3.16

# Add the specified Alpine branch repository and install OpenSSL
RUN echo "http://dl-cdn.alpinelinux.org/alpine/${ALPINE_BRANCH}/main" >> /etc/apk/repositories && \
    apk add --no-cache openssl=${OPENSSL_VERSION} wget perl build-base && \
    rm -rf /var/lib/apt/lists/*

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Kestrel.csproj", "."]
RUN dotnet restore "./Kestrel.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./Kestrel.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Kestrel.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT [ "dotnet", "Kestrel.dll" ]