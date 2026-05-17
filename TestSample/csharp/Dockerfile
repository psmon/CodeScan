FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app
COPY HelloWorld.csproj ./
RUN dotnet restore
COPY src/ src/
RUN dotnet publish -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "HelloWorld.dll"]
