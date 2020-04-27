FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-job
WORKDIR /app

# copy everything
COPY . ./

# build
WORKDIR /app/src/AzDoConsumer
RUN dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-driver1
WORKDIR /app

# copy everything
COPY . ./

# build
WORKDIR /app/src/BenchmarksDriver
RUN dotnet publish -c Release -o /app/out --self-contained -r linux-x64

FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-driver2
WORKDIR /app

# copy everything
COPY . ./

# build
WORKDIR /app/src/BenchmarksDriver2
RUN dotnet publish -c Release -o /app/out --self-contained -r linux-x64

FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS runtime

WORKDIR /app

# clone ASP.NET
RUN git clone https://github.com/dotnet/aspnetcore
WORKDIR /app/aspnetcore
RUN git submodule update --init

WORKDIR /app

COPY --from=build-job /app/out ./
COPY --from=build-driver1 /app/out ./driver1
COPY --from=build-driver2 /app/out ./driver2

ENTRYPOINT ["dotnet", "AzDoConsumer.dll", "--connection-string", "SERVICE_BUS_CONNECTION_STRING", "--queue", "SERVICE_BUS_QUEUE", "--jobs", "jobs.json"] \
