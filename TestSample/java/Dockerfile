FROM maven:3.9-eclipse-temurin-21 AS build
WORKDIR /app
COPY pom.xml ./
COPY src/ src/
RUN mvn -B package

FROM eclipse-temurin:21-jre
WORKDIR /app
COPY --from=build /app/target/helloworld-1.0.0.jar app.jar
ENTRYPOINT ["java", "-jar", "app.jar"]
