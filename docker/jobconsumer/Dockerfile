FROM mcr.microsoft.com/dotnet/core/sdk:3.0.100 AS build-job
WORKDIR /app

# copy everything
COPY . ./

# build
WORKDIR /app/src/JobConsumer
RUN dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/core/sdk:3.0.100 AS build-driver
WORKDIR /app

# copy everything
COPY . ./

# build
WORKDIR /app/src/BenchmarksDriver
RUN dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/core/sdk:3.0.100 AS runtime

WORKDIR /app

# clone ASP.NET
RUN git clone https://github.com/dotnet/aspnetcore
WORKDIR /app/aspnetcore
RUN git submodule update --init

WORKDIR /app

COPY --from=build-job /app/out ./
COPY --from=build-driver /app/out ./driver
ENTRYPOINT ["dotnet", "JobConsumer.dll", "--driver", "/app/driver/BenchmarksDriver.dll", "--repo-path", "/app/aspnetcore"]
