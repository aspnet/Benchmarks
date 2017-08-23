# wrk is currently broken in debian:stretch (4.0.2-1+b1), but fixed in debian:buster (4.0.2-2).
# This requires us to start from debian:buster and manually install the .NET Core dependencies,
# since there is currently no "2.1.0-preview1-runtime-deps" image based on debian:buster.

FROM debian:buster

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
