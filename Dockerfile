# ── Stage 1: Build ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the projects needed to build the API (faster caching, ignores test projects)
COPY BookCatalog.API/BookCatalog.API.csproj BookCatalog.API/
COPY BookCatalog.Core/BookCatalog.Core.csproj BookCatalog.Core/
COPY BookCatalog.Infrastructure/BookCatalog.Infrastructure.csproj BookCatalog.Infrastructure/

RUN dotnet restore BookCatalog.API/BookCatalog.API.csproj

# Copy the rest of the source code
COPY . .

RUN dotnet publish BookCatalog.API/BookCatalog.API.csproj -c Release -o /app/publish --no-restore

# ── Stage 2: Runtime ───────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
EXPOSE 8080

# Run as built-in non-root user for security best practices
USER $APP_UID

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "BookCatalog.API.dll"]