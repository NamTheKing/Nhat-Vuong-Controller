# Nhat Vuong Controller server: REST API + scheduler + embedded MQTT broker in one process.
# Build from the repository root:  docker build -f deploy/Dockerfile -t nhatvuong-server .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Build.targets ./
COPY src/Domain/ src/Domain/
COPY src/Application/ src/Application/
COPY src/Contracts/ src/Contracts/
COPY src/Adapters/ src/Adapters/
COPY src/Server/ src/Server/
RUN dotnet publish src/Server/NhatVuong.Server.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# curl is used only by the container health check (CAP-23).
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
# Writable home for the grant-signing key (mounted as a named volume, which inherits this ownership).
RUN mkdir -p /keys && chown app:app /keys
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080
# 8080: API behind the TLS-terminating reverse proxy; 8883: MQTT over TLS for modules.
EXPOSE 8080 8883
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
  CMD curl -fsS http://localhost:8080/health/ready || exit 1
USER app
ENTRYPOINT ["dotnet", "NhatVuong.Server.dll"]
