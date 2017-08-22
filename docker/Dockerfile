FROM microsoft/dotnet-nightly:2.1.0-preview1-runtime-deps-jessie

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        git \
        unzip \
        wget \
    && rm -rf /var/lib/apt/lists/*

RUN git clone https://github.com/aspnet/Benchmarks

RUN /Benchmarks/build.sh
