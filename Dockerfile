# ── Node build stage (Angular SPA) ────────────────────────────────────────────
FROM node:22-alpine AS node-build
WORKDIR /node-build

# Trust corporate CAs (must happen before apk/npm can reach HTTPS registries)
COPY --from=certs . /tmp/certs/
RUN find /tmp/certs/ -name '.git*' -delete 2>/dev/null || true && \
    find /tmp/certs/ -name 'README.md' -delete 2>/dev/null || true && \
    for f in /tmp/certs/*.crt /tmp/certs/*.pem; do \
      [ -f "$f" ] && cp "$f" /usr/local/share/ca-certificates/ 2>/dev/null || true; \
    done && \
    cat /tmp/certs/*.crt /tmp/certs/*.pem >> /etc/ssl/certs/ca-certificates.crt 2>/dev/null || true && \
    rm -rf /tmp/certs/

ENV NODE_EXTRA_CA_CERTS=/etc/ssl/certs/ca-certificates.crt

COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npx ng build --configuration docker

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /build

RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates openssl && rm -rf /var/lib/apt/lists/*
COPY --from=certs . /usr/local/share/ca-certificates/corporate/
RUN find /usr/local/share/ca-certificates/corporate/ -name '.git*' -delete 2>/dev/null || true && \
    find /usr/local/share/ca-certificates/corporate/ -name 'README.md' -delete 2>/dev/null || true && \
    update-ca-certificates

ENV SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt \
    SSL_CERT_DIR=/etc/ssl/certs \
    DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER=0 \
    NUGET_CERT_REVOCATION_MODE=off \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NUGET_SIGNATURE_VERIFICATION=false

COPY Directory.Build.props ./
COPY src/Andy.CodeIndex.Api/Andy.CodeIndex.Api.csproj src/Andy.CodeIndex.Api/
COPY src/Andy.CodeIndex.Application/Andy.CodeIndex.Application.csproj src/Andy.CodeIndex.Application/
COPY src/Andy.CodeIndex.Domain/Andy.CodeIndex.Domain.csproj src/Andy.CodeIndex.Domain/
COPY src/Andy.CodeIndex.Infrastructure/Andy.CodeIndex.Infrastructure.csproj src/Andy.CodeIndex.Infrastructure/
COPY src/Andy.CodeIndex.Shared/Andy.CodeIndex.Shared.csproj src/Andy.CodeIndex.Shared/
RUN dotnet restore src/Andy.CodeIndex.Api/Andy.CodeIndex.Api.csproj

COPY . .
RUN dotnet publish src/Andy.CodeIndex.Api/Andy.CodeIndex.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl openssl git && rm -rf /var/lib/apt/lists/*

# Copy corporate CA certs and install them
COPY --from=certs . /tmp/certs/
RUN find /tmp/certs/ -name '.git*' -delete 2>/dev/null || true && \
    find /tmp/certs/ -name 'README.md' -delete 2>/dev/null || true && \
    find /tmp/certs/ -name '.gitkeep' -delete 2>/dev/null || true && \
    find /tmp/certs/ -name '.gitignore' -delete 2>/dev/null || true && \
    mkdir -p /usr/local/share/ca-certificates/corporate && \
    for f in /tmp/certs/*.pem /tmp/certs/*.crt /tmp/certs/*.cer; do \
      [ -f "$f" ] || continue; \
      cp "$f" /usr/local/share/ca-certificates/corporate/"$(basename "$f").crt" 2>/dev/null || true; \
      cat "$f" >> /etc/ssl/certs/ca-certificates.crt 2>/dev/null || true; \
    done && \
    update-ca-certificates 2>/dev/null || true && \
    rm -rf /tmp/certs/

# Configure git to trust the system CA bundle and /data repos
RUN git config --system http.sslCAInfo /etc/ssl/certs/ca-certificates.crt && \
    git config --system --add safe.directory '*'

# Non-root user
RUN groupadd -r codeindex && useradd -r -g codeindex -d /app -s /sbin/nologin codeindex
RUN mkdir -p /data /https /app/.aspnet/DataProtection-Keys && \
    chown codeindex:codeindex /data /app/.aspnet/DataProtection-Keys

COPY --from=build /app/publish .
COPY --from=node-build /node-build/dist/client/browser ./wwwroot
RUN chown -R codeindex:codeindex /app

# Self-signed dev cert
RUN openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
      -keyout /tmp/dev.key -out /tmp/dev.crt \
      -subj "/CN=localhost" -addext "subjectAltName=DNS:localhost,IP:127.0.0.1" && \
    openssl pkcs12 -export -out /https/aspnetapp.pfx \
      -inkey /tmp/dev.key -in /tmp/dev.crt -passout pass:devcert && \
    rm -f /tmp/dev.key /tmp/dev.crt && \
    chown codeindex:codeindex /https/aspnetapp.pfx

RUN printf '#!/bin/sh\nset -e\nif ls /usr/local/share/ca-certificates/custom/*.crt 1>/dev/null 2>&1 || ls /usr/local/share/ca-certificates/custom/*.pem 1>/dev/null 2>&1; then\n    for f in /usr/local/share/ca-certificates/custom/*.pem; do\n        [ -f "$f" ] && cat "$f" >> /etc/ssl/certs/ca-certificates.crt 2>/dev/null || true\n    done\n    update-ca-certificates 2>/dev/null || true\nfi\nexec "$@"\n' > /docker-entrypoint.sh && \
    chmod +x /docker-entrypoint.sh

ENV SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt \
    SSL_CERT_DIR=/etc/ssl/certs \
    ASPNETCORE_Kestrel__Certificates__Default__Path=/https/aspnetapp.pfx \
    ASPNETCORE_Kestrel__Certificates__Default__Password=devcert \
    Indexing__DataDir=/data

EXPOSE 8080
USER codeindex

ENTRYPOINT ["/docker-entrypoint.sh"]
CMD ["dotnet", "Andy.CodeIndex.Api.dll"]
