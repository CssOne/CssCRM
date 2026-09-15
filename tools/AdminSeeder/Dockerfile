# Build a partir da raiz do repositório: docker build -f tools/AdminSeeder/Dockerfile -t admin-seeder .
# Ferramenta de uso único/pontual — cria (ou promove a Admin) um usuário real de produção.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY tools/AdminSeeder/AdminSeeder.csproj tools/AdminSeeder/
COPY src/CssVision.Web/CssVision.Web.csproj src/CssVision.Web/
RUN dotnet restore tools/AdminSeeder/AdminSeeder.csproj

COPY tools/AdminSeeder/ tools/AdminSeeder/
COPY src/CssVision.Web/ src/CssVision.Web/
RUN dotnet publish tools/AdminSeeder/AdminSeeder.csproj -c Release -o /app/publish --no-restore /p:PublishClientApp=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AdminSeeder.dll"]
