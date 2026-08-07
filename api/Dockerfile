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
USER root
RUN mkdir -p /keys /app/.local /app/Files \
    && chown $APP_UID:$APP_UID /keys /app /app/.local /app/Files
USER $APP_UID

ENTRYPOINT ["dotnet", "ClinicManagement.API.dll"]











