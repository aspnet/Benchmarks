FROM microsoft/dotnet:2.1-sdk

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
# dotnet dependencies?
        libunwind-dev \
# wrk dependencies
        build-essential \
        libssl-dev \
# KoreBuild dependencies
        unzip \
        wget \
# BenchmarksServer dependencies
        git \
        procps \
# Perfview dependencies
        zip \
        linux-tools \
        lttng-tools \
        liblttng-ust-dev \
        binutils \
# make dependencies
        flex \
        bison \
    && rm -rf /var/lib/apt/lists/*

# Make perf visible for perfcollect
RUN cp /usr/bin/perf_4.9 /usr/bin/perf

# Build and install wrk
ENV WRK_VERSION 4.1.0
RUN cd /tmp \
    && wget https://github.com/wg/wrk/archive/$WRK_VERSION.tar.gz -O wrk.tar.gz \
    && tar xvzf wrk.tar.gz \
    && cd wrk-$WRK_VERSION \
    && make \
    && cp wrk /usr/local/bin \
    && cd .. \
    && rm -rf wrk.tar.gz wrk-$WRK_VERSION

# Install docker client
ENV DOCKER_VERSION 17.09.0-ce
RUN cd /tmp \
    && wget https://download.docker.com/linux/static/stable/x86_64/docker-$DOCKER_VERSION.tgz -O docker.tgz \
    && tar xvzf docker.tgz \
    && cp docker/docker /usr/bin \
    && rm -rf docker.tgz docker

WORKDIR /benchmarks

ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE true

COPY . .
