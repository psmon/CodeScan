FROM golang:1.22-alpine AS build
WORKDIR /src
COPY go.mod ./
COPY person/ person/
COPY speakers/ speakers/
COPY world/ world/
COPY main.go ./
RUN CGO_ENABLED=0 go build -o /out/helloworld ./

FROM alpine:3.20
WORKDIR /app
COPY --from=build /out/helloworld ./
ENTRYPOINT ["./helloworld"]
