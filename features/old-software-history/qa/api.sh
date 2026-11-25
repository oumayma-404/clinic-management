#!/usr/bin/env bash
# Runs the PR #123 API build against the throwaway DB. Usage: pr123-api.sh <port> <boostUntilIso|-> [verb args...]
# Local QA only: own DB (clinic_pr123_qa), own bucket (pr123-qa), own key ring, console off.
PORT="$1"; UNTIL="$2"; shift 2
S="${PR123_SCRATCH:?set PR123_SCRATCH to the folder holding pr123bin and pr123-keys}"
cd "$S/pr123bin/Release/net8.0" || exit 1
export ASPNETCORE_ENVIRONMENT=Development
export Hosting__Urls="http://localhost:$PORT" ASPNETCORE_URLS="http://localhost:$PORT"
export Console__Port=0 FrontendUrl=http://localhost:3095 MinIO__BucketName=pr123-qa Diagnostics__SlowRequestMs=0
export DataProtection__KeyRingPath="$S/pr123-keys"
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=clinic_pr123_qa;Username=clinic_user;Password=clinic_password"
export RateLimiting__Api__PermitLimit=120
if [ "$UNTIL" != "-" ]; then
  export RateLimiting__Api__Boost__Email=import@pr123.test RateLimiting__Api__Boost__Until="$UNTIL" RateLimiting__Api__Boost__PermitLimit=600
fi
exec dotnet ClinicManagement.API.dll "$@"
