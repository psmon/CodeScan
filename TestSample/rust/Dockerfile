FROM rust:1.79-alpine AS build
RUN apk add --no-cache musl-dev
WORKDIR /src
COPY Cargo.toml ./
COPY src/ src/
RUN cargo build --release

FROM alpine:3.20
WORKDIR /app
COPY --from=build /src/target/release/helloworld ./
ENTRYPOINT ["./helloworld"]
