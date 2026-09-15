# Build a partir da raiz do repositório: docker build -f Dockerfile .

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# O target PublishClientApp do .csproj roda "npm run build" durante o dotnet publish — precisa de
# Node.js disponível nesta imagem (a imagem do SDK do .NET não vem com ele).
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

COPY src/CssVision.Web/CssVision.Web.csproj src/CssVision.Web/
RUN dotnet restore src/CssVision.Web/CssVision.Web.csproj

COPY src/CssVision.Web/ src/CssVision.Web/
RUN dotnet publish src/CssVision.Web/CssVision.Web.csproj -c Release -o /app/publish --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl só é usado pelo healthcheck do docker-compose (docker-compose.prod.yml).
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && adduser --disabled-password --gecos "" appuser
COPY --from=build /app/publish .
# uploads/ só é escrito de fato quando Storage:S3:BucketName não está configurado (ver
# LocalFileStorageService) — mesmo em produção com S3, mantém a pasta pronta como fallback.
RUN mkdir -p /app/wwwroot/uploads && chown -R appuser:appuser /app
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CssVision.Web.dll"]
