###############################################################################
# Stage 1 — Build
###############################################################################
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
ARG TARGETARCH=arm64

WORKDIR /src
COPY src/NestorBridge/NestorBridge.csproj src/NestorBridge/
RUN dotnet restore src/NestorBridge/NestorBridge.csproj -r linux-${TARGETARCH}

COPY src/NestorBridge/ src/NestorBridge/
RUN dotnet publish src/NestorBridge/NestorBridge.csproj \
  -c Release \
  -r linux-${TARGETARCH} \
  --self-contained false \
  -o /app/publish

###############################################################################
# Stage 2 — Runtime
###############################################################################
FROM mcr.microsoft.com/dotnet/runtime:8.0-bookworm-slim-arm64v8 AS runtime

# Install s6-overlay (HA add-on requirement)
ARG S6_OVERLAY_VERSION=3.1.6.2
ADD https://github.com/just-containers/s6-overlay/releases/download/v${S6_OVERLAY_VERSION}/s6-overlay-noarch.tar.xz /tmp
ADD https://github.com/just-containers/s6-overlay/releases/download/v${S6_OVERLAY_VERSION}/s6-overlay-aarch64.tar.xz /tmp
RUN tar -C / -Jxpf /tmp/s6-overlay-noarch.tar.xz \
  && tar -C / -Jxpf /tmp/s6-overlay-aarch64.tar.xz \
  && rm -f /tmp/s6-overlay-*.tar.xz

# Copy published app
COPY --from=build /app/publish /app

# Copy s6 service definitions
COPY rootfs/ /

ENTRYPOINT ["/init"]
