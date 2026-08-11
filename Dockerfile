# syntax=docker/dockerfile:1.7
# Multi-stage build for Portside. Build from the repo root so the rd-ui submodule
# (src/web/portside/src/rd-ui) is available. The API publishes as a Native AOT
# binary, so the final image needs only runtime-deps.
#
#   docker build -t portside:dev .
#   docker run -p 8080:8080 -v $HOME/.kube:/home/app/.kube:ro portside:dev

ARG DOTNET_VERSION=10.0
ARG NODE_VERSION=22

# ---------- Frontend build ----------
FROM node:${NODE_VERSION}-bookworm-slim AS web-build
WORKDIR /web

# Install rd-ui submodule deps and build the library first.
COPY src/web/portside/src/rd-ui/package*.json src/web/portside/src/rd-ui/
RUN cd src/web/portside/src/rd-ui && npm ci

# App deps next.
COPY src/web/portside/package*.json src/web/portside/
RUN cd src/web/portside && npm ci

# Copy source for both the rd-ui submodule and the portside app, then build.
COPY src/web/portside/src/rd-ui src/web/portside/src/rd-ui
COPY src/web/portside src/web/portside
RUN cd src/web/portside && npm run prod

# ---------- Backend build (Native AOT) ----------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS api-build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Native AOT compilation needs a native toolchain in the SDK image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

COPY src/api/PortsideApi.csproj src/api/
COPY common.props Directory.Build.props ./
RUN dotnet restore src/api/PortsideApi.csproj -r linux-x64 -p:SkipSpa=true

COPY src/api src/api

# SkipSpa: the Angular bundle is built in the web-build stage; the API publishes alone.
RUN dotnet publish src/api/PortsideApi.csproj \
    -c $BUILD_CONFIGURATION \
    -r linux-x64 \
    -p:SkipSpa=true \
    -o /app/publish \
    && rm -f /app/publish/*.dbg

# ---------- Final runtime ----------
# Native binary needs only runtime-deps (no .NET runtime in the image).
FROM mcr.microsoft.com/dotnet/runtime-deps:${DOTNET_VERSION} AS final
WORKDIR /app

# Copy publish output and the prebuilt SPA into wwwroot.
COPY --from=api-build /app/publish .
COPY --from=web-build /web/src/web/portside/dist/portside/browser ./wwwroot

# Writable mount points: SQLite db lives in /app/data (override via volume / k8s PVC).
RUN mkdir -p /app/data /app/wwwroot \
    && chown -R app:app /app

USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__DefaultConnection="Data Source=/app/data/portside.db"
EXPOSE 8080
ENTRYPOINT ["./PortsideApi"]
