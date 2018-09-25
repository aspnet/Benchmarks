FROM microsoft/dotnet:2.1-sdk AS build-env
WORKDIR /app

# copy everything
COPY . ./

# build
WORKDIR /app/src/Proxy
RUN dotnet publish -c Release -o /app/out

# create runtime image
FROM microsoft/dotnet:2.1-aspnetcore-runtime
WORKDIR /app
COPY --from=build-env /app/out ./
ENTRYPOINT ["dotnet", "Proxy.dll"]
