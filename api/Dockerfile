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
ENTRYPOINT ["dotnet", "ClinicManagement.API.dll"]

