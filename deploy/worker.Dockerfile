# The ingestion worker, built from the repository root:
#     docker build -f deploy/worker.Dockerfile -t coe-worker .
#
# Same three directories as the API image: it is the process that compiles domain/ into
# templates and pulls B3's exports into reference/b3/.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Coe.Core/Coe.Core.csproj src/Coe.Core/
COPY src/Coe.Ingestion/Coe.Ingestion.csproj src/Coe.Ingestion/
COPY src/Coe.Clearing/Coe.Clearing.csproj src/Coe.Clearing/
COPY src/Coe.Infrastructure/Coe.Infrastructure.csproj src/Coe.Infrastructure/
COPY src/Coe.Observability/Coe.Observability.csproj src/Coe.Observability/
COPY src/Coe.Worker/Coe.Worker.csproj src/Coe.Worker/
RUN dotnet restore src/Coe.Worker/Coe.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Coe.Worker/Coe.Worker.csproj \
    --configuration Release --no-restore --output /app/publish

# ICU again: the worker opens the same SQL connections the API does.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
COPY db/ /app/db/
COPY domain/ /app/domain/
COPY reference/b3/ /app/reference/b3/

# The worker writes both: the CETIP exports it fetches, and any figure dropped into domain/.
RUN chown --recursive $APP_UID:$APP_UID /app/reference /app/domain

ENV DOTNET_ENVIRONMENT=Production \
    Database__ScriptDirectory=/app/db \
    Ingestion__DomainDirectory=/app/domain \
    Ingestion__ReferenceDirectory=/app/reference/b3

USER $APP_UID
ENTRYPOINT ["dotnet", "Coe.Worker.dll"]
