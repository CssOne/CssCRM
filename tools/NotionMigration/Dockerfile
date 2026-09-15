# Build a partir da raiz do repositório: docker build -f tools/NotionMigration/Dockerfile -t notion-migration .
# Ferramenta de uso pontual — importa/enriquece leads a partir do Notion. Modo controlado por
# variáveis de ambiente (MIGRATION_MODE, MIGRATION_STATUS_FILTER etc. — ver Program.cs).

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY tools/NotionMigration/NotionMigration.csproj tools/NotionMigration/
COPY src/CssVision.Web/CssVision.Web.csproj src/CssVision.Web/
RUN dotnet restore tools/NotionMigration/NotionMigration.csproj

COPY tools/NotionMigration/ tools/NotionMigration/
COPY src/CssVision.Web/ src/CssVision.Web/
RUN dotnet publish tools/NotionMigration/NotionMigration.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NotionMigration.dll"]
