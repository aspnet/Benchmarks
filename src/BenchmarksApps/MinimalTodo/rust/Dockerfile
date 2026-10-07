FROM rust:1.96-bookworm AS build
WORKDIR /app
COPY Cargo.toml Cargo.lock rust-toolchain.toml ./
COPY src ./src
COPY tests ./tests
RUN cargo build --release --locked

FROM build AS test
RUN cargo test --release --locked

FROM ubuntu:26.04 AS runtime
RUN groupadd --gid 10001 app && useradd --uid 10001 --gid app --no-create-home --shell /usr/sbin/nologin app
WORKDIR /app
COPY --from=build /app/target/release/minimal-todo /app/minimal-todo
USER 10001:10001
ENV PORT=8080
EXPOSE 8080
STOPSIGNAL SIGTERM
ENTRYPOINT ["/app/minimal-todo"]
