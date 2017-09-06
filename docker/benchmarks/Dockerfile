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
ENV WRK_VERSION 4.0.2
RUN cd /tmp \
    && wget https://github.com/wg/wrk/archive/$WRK_VERSION.tar.gz -O wrk.tar.gz \
    && tar xvzf wrk.tar.gz \
    && cd wrk-$WRK_VERSION \
    && make \
    && cp wrk /usr/local/bin \
    && cd .. \
    && rm -rf wrk.tar.gz wrk-$WRK_VERSION

WORKDIR /benchmarks
COPY . .
RUN /benchmarks/build.sh
