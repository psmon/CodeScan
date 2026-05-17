FROM alpine:3.20 AS build
RUN apk add --no-cache build-base cmake
WORKDIR /src
COPY CMakeLists.txt ./
COPY src/ src/
RUN cmake -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build

FROM alpine:3.20
RUN apk add --no-cache libstdc++
WORKDIR /app
COPY --from=build /src/build/helloworld ./
ENTRYPOINT ["./helloworld"]
