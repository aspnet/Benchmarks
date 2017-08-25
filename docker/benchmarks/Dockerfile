# The ideal base image would be "microsoft/dotnet-nightly:2.1.0-preview1-runtime-deps-stretch".
# However, the wrk package is currently broken in "debian:stretch" (4.0.2-1+b1), but fixed in
# "debian:buster" (4.0.2-2).  For now, it's easier to use "debian:buster" as the base image and 
# manually install the .NET Core dependencies, rather than using "microsoft/dotnet-nightly" and
# compiling wrk from source.

FROM debian:buster

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
# .NET Core dependencies
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
# KoreBuild dependencies
        unzip \
        wget \
# BenchmarksClient dependencies
        wrk \
# BenchmarksServer dependencies
        git \
        procps \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /benchmarks
COPY . .
RUN /benchmarks/build.sh
