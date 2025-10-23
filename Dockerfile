# Multi-stage build for the gateway.
#   docker build -t llm-gateway .
#   docker compose up        # gateway + Redis + Jaeger, see docker-compose.yml

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so the package layer is reused when only source files change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/LlmGateway/LlmGateway.csproj src/LlmGateway/
RUN dotnet restore src/LlmGateway/LlmGateway.csproj

COPY src/LlmGateway/ src/LlmGateway/
RUN dotnet publish src/LlmGateway/LlmGateway.csproj \
        --configuration Release \
        --no-restore \
        --output /app \
        -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app ./

# The usage database is created and migrated on first start under /app/data,
# the only path the unprivileged user can write to. Mount a volume there to keep it.
RUN mkdir -p /app/data && chown "$APP_UID" /app/data

USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1

EXPOSE 8080

ENTRYPOINT ["dotnet", "LlmGateway.dll"]
