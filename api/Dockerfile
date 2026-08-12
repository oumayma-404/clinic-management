FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["ClinicManagement.API/ClinicManagement.API.csproj", "ClinicManagement.API/"]
COPY ["ClinicManagement.Application/ClinicManagement.Application.csproj", "ClinicManagement.Application/"]
COPY ["ClinicManagement.Domain/ClinicManagement.Domain.csproj", "ClinicManagement.Domain/"]
COPY ["ClinicManagement.Infrastructure/ClinicManagement.Infrastructure.csproj", "ClinicManagement.Infrastructure/"]

RUN dotnet restore "ClinicManagement.API/ClinicManagement.API.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/ClinicManagement.API"
RUN dotnet build "ClinicManagement.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ClinicManagement.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# The PostgreSQL CLIENT tools, so the in-app backup works with NO operator configuration.
#
# ⚠️ Without this the whole backup subsystem was dead on every Docker deployment and reported itself present at
# every other layer: `Backup:PgDumpPath` ships as `""` and is written by exactly one of the four ways this product
# is deployed (the Windows installer, which bundles its own PostgreSQL), so « Sauvegarder maintenant », the hourly
# `BackupJob`, the pre-migration safety dump and the `restore-backup` verb all answered « L'outil pg_dump est
# introuvable » — for the life of the product. `PostgresToolLocator` searches PATH, which is why nothing needs
# configuring once the binaries are here.
#
# ⚠️ Version 16 SPECIFICALLY, from PGDG rather than Debian's own archive: `pg_dump` refuses a server whose major
# version is newer than its own, and bookworm ships the 15 client. It must track the `postgres:16-alpine` server in
# `deploy/`, so bumping one means bumping the other.
#
# The codename is read from the base image rather than hardcoded, so a bookworm→trixie base bump does not silently
# point at a repository suite that does not exist. `curl`/`gnupg` are purged again in the same layer.
USER root
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends curl ca-certificates gnupg; \
    codename="$(. /etc/os-release && echo "$VERSION_CODENAME")"; \
    install -d /usr/share/postgresql-common/pgdg; \
    curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc \
        -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc; \
    echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${codename}-pgdg main" \
        > /etc/apt/sources.list.d/pgdg.list; \
    apt-get update; \
    apt-get install -y --no-install-recommends postgresql-client-16; \
    apt-get purge -y --auto-remove curl gnupg; \
    rm -rf /var/lib/apt/lists/*; \
    pg_dump --version; \
    pg_restore --version

# Runs as a non-root user (review finding 28). This is the container that mounts the Data Protection key ring and
# holds the Postgres and MinIO root credentials in its environment, so any RCE or escape was immediately
# root-with-the-keys — while the `web` container had correctly dropped to uid 1001 all along. `APP_UID` (1654) is
# defined by the aspnet base image, which does not switch to it on its own.
#
# ⚠️ /keys is created and chowned HERE, before the named volume mounts over it. Docker propagates an image
# directory's ownership to a *new* empty volume, which is what makes the key ring writable without a privileged
# init step; an already-root-owned volume from an earlier deploy needs a one-off `chown -R 1654:1654` instead.
#
# ⚠️ `/app/.local` and `/app/Files` are here for the SAME reason and were missing: `SelfHostedLan`
# (`deploy/docker-compose.selfhosted-lan.yml`) mounts named volumes over both — the self-generated CA and server
# certificate, and the patient blobs `LocalDiskFileStorage` writes. Neither path exists in the image, so Docker
# created them **owned by root** and the non-root process crash-looped on
# « Access to the path '/app/.local/dataprotection-keys' is denied ». Creating and chowning them first is what
# makes the ownership propagate to the new volume.
#
# ⚠️ `/app/Backups` is here for the same reason and was missing: `deploy/docker-compose.selfhosted-lan.yml` mounts
# a named volume over it (`Backup__DefaultDestination`), and a backup written into the container's own writable
# layer instead is destroyed by the next `docker compose up` — a backup that does not survive a redeploy is not a
# backup, and nothing would have said so.
USER root
RUN mkdir -p /keys /app/.local /app/Files /app/Backups \
    && chown $APP_UID:$APP_UID /keys /app /app/.local /app/Files /app/Backups
USER $APP_UID

ENTRYPOINT ["dotnet", "ClinicManagement.API.dll"]











