FROM microsoft/dotnet-nightly:2.1.0-preview1-runtime-deps-stretch

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        git \
# Required for KoreBuild
        unzip \
        wget \
# Required for BenchmarksClient
        wrk \
# Required for "pkill" which is used by BenchmarksServer
        procps \
    && rm -rf /var/lib/apt/lists/*

RUN git clone https://github.com/aspnet/Benchmarks

ENV KOREBUILD_SKIP_OLD_RUNTIME_INSTALL 1

RUN /Benchmarks/build.sh
