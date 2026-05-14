# Local override of TechEmpower/FrameworkBenchmarks frameworks/Rust/ntex/ntex-db.dockerfile.
#
# See docker/ntex-techempower/ntex-plt.dockerfile for the rationale (in short:
# ntex-bytes 1.6 broke compatibility with the fafhrd91/postgres ntex-3 tokio-postgres
# fork, so we pin ntex-bytes to =1.5.4 here).
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

CMD ./target/release/ntex-db
