FROM microsoft/dotnet-nightly:2.1.0-preview1-runtime-deps-jessie

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
# wrk dependencies
        build-essential \
        libssl-dev \
# KoreBuild dependencies
        unzip \
        wget \
# BenchmarksServer dependencies
        git \
        procps \
    && rm -rf /var/lib/apt/lists/*

# Build and install wrk
RUN git clone https://github.com/wg/wrk.git wrk \
    && cd wrk \
    && make \
    && cp wrk /usr/local/bin \
    && cd / \
    && rm -rf wrk

WORKDIR /benchmarks
COPY . .
RUN /benchmarks/build.sh
