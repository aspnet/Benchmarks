# Local override of TechEmpower/FrameworkBenchmarks frameworks/Rust/ntex/ntex-plt.dockerfile.
#
# Why we override: The upstream Cargo.toml uses `ntex-bytes = { version = "1.5", ... }`
# (semver caret), so cargo resolves to the latest 1.x. ntex-bytes 1.6.0 (released
# 2026-05-02) introduced the BytePages type, which is incompatible with the
# fafhrd91/postgres `ntex-3` tokio-postgres fork that still uses BytesMut. All
# ntex binaries share one workspace and `cargo build --features="tokio"` pulls
# tokio-postgres in transitively, so the workspace fails to compile.
#
# Workaround: clone TechEmpower's master branch from inside the container and pin
# ntex-bytes to =1.5.4 before building. Drop this override once the upstream
# fafhrd91/postgres fork is updated for ntex-bytes 1.6+ or TechEmpower pins
# ntex-bytes themselves.
FROM rust:1.93

# Disable simd at jsonescape
# ENV CARGO_CFG_JSONESCAPE_DISABLE_AUTO_SIMD=

RUN apt-get update -yqq && apt-get install -yqq cmake g++ git

ARG TECHEMPOWER_REF=master

WORKDIR /tmp
RUN git clone --depth=1 --branch ${TECHEMPOWER_REF} \
    https://github.com/TechEmpower/FrameworkBenchmarks.git framework-benchmarks \
    && mkdir -p /ntex \
    && cp -a framework-benchmarks/frameworks/Rust/ntex/. /ntex/ \
    && rm -rf framework-benchmarks

WORKDIR /ntex

# Pin ntex-bytes to =1.5.4 so the workspace stays on the BytesMut-compatible API
# used by the fafhrd91/postgres ntex-3 tokio-postgres fork.
RUN sed -i 's|ntex-bytes = { version = "1.5", features=\["simd"\] }|ntex-bytes = { version = "=1.5.4", features=["simd"] }|' Cargo.toml \
    && grep '^ntex-bytes' Cargo.toml

RUN cargo clean
RUN RUSTFLAGS="-C target-cpu=native" cargo build --release --features="tokio"

EXPOSE 8080

CMD ./target/release/ntex-plt
