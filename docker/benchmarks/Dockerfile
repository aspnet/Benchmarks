# Replace with "FROM microsoft/dotnet-nightly:2.1.0-preview1-runtime-deps-stretch" once
# wrk 4.0.2-2 is available in debian:stable
FROM debian:testing

# .NET Core dependencies
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
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
# KoreBuild dependencies
        unzip \
        wget \
# BenchmarksClient dependencies
        wrk \
# BenchmarksServer dependencies
        git \
        procps \
    && rm -rf /var/lib/apt/lists/*

# Runtimes prior to 2.0.0 are not supported on Debian Stretch, but they should also
# not be required for Benchmarks
ENV KOREBUILD_SKIP_OLD_RUNTIME_INSTALL 1

WORKDIR /benchmarks
COPY . .
RUN /benchmarks/build.sh
