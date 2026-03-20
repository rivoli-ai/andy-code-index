# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy build config and csproj files for restore (layer caching)
COPY ["Directory.Build.props", "./"]
COPY ["src/Andy.CodeIndex.Api/Andy.CodeIndex.Api.csproj", "src/Andy.CodeIndex.Api/"]
COPY ["src/Andy.CodeIndex.Application/Andy.CodeIndex.Application.csproj", "src/Andy.CodeIndex.Application/"]
COPY ["src/Andy.CodeIndex.Domain/Andy.CodeIndex.Domain.csproj", "src/Andy.CodeIndex.Domain/"]
COPY ["src/Andy.CodeIndex.Infrastructure/Andy.CodeIndex.Infrastructure.csproj", "src/Andy.CodeIndex.Infrastructure/"]
COPY ["src/Andy.CodeIndex.Shared/Andy.CodeIndex.Shared.csproj", "src/Andy.CodeIndex.Shared/"]
RUN dotnet restore "src/Andy.CodeIndex.Api/Andy.CodeIndex.Api.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/src/Andy.CodeIndex.Api"
RUN dotnet build "Andy.CodeIndex.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Andy.CodeIndex.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create non-root user
RUN groupadd -r codeindex && useradd -r -g codeindex -d /app -s /sbin/nologin codeindex

# Create data directory for repository clones
RUN mkdir -p /data && chown codeindex:codeindex /data

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV Indexing__DataDir=/data

COPY --from=publish /app/publish .
RUN chown -R codeindex:codeindex /app

USER codeindex

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Andy.CodeIndex.Api.dll"]
