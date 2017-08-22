# Replace with "FROM microsoft/dotnet-nightly:2.1.0-preview1-runtime-deps-stretch" once
# wrk 4.0.2-2 is available in debian:stable
FROM debian:testing

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        \
# .NET Core dependencies
        libc6 \
        libcurl3 \
        libgcc1 \
        libgssapi-krb5-2 \
        libicu57 \
        liblttng-ust0 \
        libssl1.0.2 \
        libstdc++6 \
        libunwind8 \
        libuuid1 \
        zlib1g \
    && rm -rf /var/lib/apt/lists/*

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        git \
# KoreBuild dependencies
        unzip \
        wget \
# BenchmarksClient dependencies
        wrk \
#BenchmarksServer dependencies (pkill)
        procps \
    && rm -rf /var/lib/apt/lists/*

RUN git clone https://github.com/aspnet/Benchmarks

ENV KOREBUILD_SKIP_OLD_RUNTIME_INSTALL 1

RUN /Benchmarks/build.sh
