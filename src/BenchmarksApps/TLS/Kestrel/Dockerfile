# Arguments for the app itself
ARG urls=https://0.0.0.0:8080
ENV urlsEnv=$urls
ARG mTLS=false
ARG certValidationConsoleEnabled=false
ARG statsEnabled=false
ARG tlsRenegotiation=false

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
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

ENTRYPOINT [ "dotnet", "Kestrel.dll", "--urls", $urlsEnv ]

# ENTRYPOINT [ "dotnet", "Kestrel.dll",                                   \
#     # behavioral args 
#     #"--urls", $urlsEnv,                                                    \
#     "--urls", $urlsEnv      \
#     # "--mTLS", $mTLS,                                                    \
#     # "--tlsRenegotiation", $tlsRenegotiation,                            \
#     # # debug args 
#     # "--certValidationConsoleEnabled", $certValidationConsoleEnabled,    \
#     # "--statsEnabled", $statsEnabled                                     \
# ]