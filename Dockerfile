# ── Stage 1: Build ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first (for layer caching on restore)
COPY BookCatalog.API.slnx ./
COPY BookCatalog.API/BookCatalog.API.csproj BookCatalog.API/
COPY BookCatalog.Core/BookCatalog.Core.csproj BookCatalog.Core/
COPY BookCatalog.Infrastructure/BookCatalog.Infrastructure.csproj BookCatalog.Infrastructure/
COPY BookCatalog.Tests/BookCatalog.Tests.csproj BookCatalog.Tests/

RUN dotnet restore BookCatalog.API.slnx

# Copy the rest of the source code
COPY . .

RUN dotnet publish BookCatalog.API/BookCatalog.API.csproj -c Release -o /app/publish --no-restore

# ── Stage 2: Runtime ───────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "BookCatalog.API.dll"]
