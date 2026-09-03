# The API image, built from the repository root:
#     docker build -f deploy/api.Dockerfile -t coe-api .
#
# It carries the three directories the host reads from disk — db/ (applied at startup),
# domain/ (the figure catalogue) and reference/b3/ (B3's published exports) — so the image
# runs against nothing but a SQL Server.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore from the manifests alone, so editing a file does not re-download the package graph.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Coe.Core/Coe.Core.csproj src/Coe.Core/
COPY src/Coe.Ingestion/Coe.Ingestion.csproj src/Coe.Ingestion/
COPY src/Coe.Clearing/Coe.Clearing.csproj src/Coe.Clearing/
COPY src/Coe.Infrastructure/Coe.Infrastructure.csproj src/Coe.Infrastructure/
COPY src/Coe.Observability/Coe.Observability.csproj src/Coe.Observability/
COPY src/Coe.Api/Coe.Api.csproj src/Coe.Api/
RUN dotnet restore src/Coe.Api/Coe.Api.csproj

COPY src/ src/
RUN dotnet publish src/Coe.Api/Coe.Api.csproj \
    --configuration Release --no-restore --output /app/publish

# The Debian runtime image, not an Alpine or chiseled one: Microsoft.Data.SqlClient throws
# "Globalization Invariant Mode is not supported" on its first connection without ICU.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
COPY db/ /app/db/
COPY domain/ /app/domain/
COPY reference/b3/ /app/reference/b3/

# The CETIP sync writes the exports it fetches back into the reference directory, so the
# runtime user owns it; mount a volume there to keep what it fetched across restarts.
RUN chown --recursive $APP_UID:$APP_UID /app/reference

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Database__ScriptDirectory=/app/db \
    Ingestion__DomainDirectory=/app/domain \
    Ingestion__ReferenceDirectory=/app/reference/b3

EXPOSE 8080
USER $APP_UID

# No HEALTHCHECK: the runtime image ships without an HTTP client, and installing one to poll
# a port would be a package to keep patched for it. The probes are on the API itself —
# /health/live and /health/ready — for whatever runs the container to call.
ENTRYPOINT ["dotnet", "Coe.Api.dll"]
