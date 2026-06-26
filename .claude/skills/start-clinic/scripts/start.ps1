#requires -Version 5.1
<#
.SYNOPSIS
    Launch the clinic-management stack locally, end to end.
.DESCRIPTION
    Brings everything up in dependency order and waits for each tier to be healthy:
      1. Docker services  -> Postgres (5432) + MinIO (9000/9001)
      2. .NET API         -> http://localhost:5000 (EF migrations auto-apply on startup; AI via HuggingFace)
      3. Next.js frontend -> http://localhost:3000
    Idempotent: skips a tier that is already listening on its port.
    API and frontend are started as detached background processes; their output is
    redirected to log files (path printed at the end).
.PARAMETER SkipDocker
    Do not touch Docker (use when Postgres/MinIO are already running elsewhere).
.PARAMETER Reset
    Recreate Docker volumes (DROPS the database) before starting.
#>
[CmdletBinding()]
param(
    [switch]$SkipDocker,
    [switch]$Reset
)

$ErrorActionPreference = 'Stop'

# --- locate project root (walk up until docker-compose.yml is found) ---
function Find-ProjectRoot {
    $dir = $PSScriptRoot
    for ($i = 0; $i -lt 8; $i++) {
        if (Test-Path (Join-Path $dir 'docker-compose.yml')) { return $dir }
        $dir = Split-Path -Parent $dir
    }
    throw "Could not find project root (docker-compose.yml) above $PSScriptRoot"
}

$Root    = Find-ProjectRoot
$ApiDir  = Join-Path $Root 'api\ClinicManagement.API'
$WebDir  = Join-Path $Root 'web'
$LogDir  = Join-Path $env:TEMP 'clinic-management-run'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

function Info($m)  { Write-Host "[start] $m"        -ForegroundColor Cyan }
function Ok($m)    { Write-Host "[ ok  ] $m"        -ForegroundColor Green }
function Warn($m)  { Write-Host "[warn ] $m"        -ForegroundColor Yellow }

function Test-Listening([int]$Port) {
    try { return [bool](Test-NetConnection -ComputerName 127.0.0.1 -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue) }
    catch { return $false }
}

function Wait-Tcp([int]$Port, [string]$Name, [int]$TimeoutSec = 90) {
    Info "Waiting for $Name on port $Port ..."
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        if (Test-Listening $Port) { Ok "$Name is up (port $Port)"; return $true }
        Start-Sleep -Seconds 2
    }
    Warn "$Name did not come up within ${TimeoutSec}s (port $Port). Check logs."
    return $false
}

function Wait-Http([string]$Url, [string]$Name, [int]$TimeoutSec = 120) {
    Info "Waiting for $Name at $Url ..."
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) { Ok "$Name is responding ($Url)"; return $true }
        } catch {
            # connection refused / not ready yet -> keep polling
        }
        Start-Sleep -Seconds 3
    }
    Warn "$Name did not respond within ${TimeoutSec}s ($Url). Check logs."
    return $false
}

Info "Project root: $Root"
Info "Logs: $LogDir"

# ----------------------------------------------------------------------
# 1) Docker: Postgres + MinIO
# ----------------------------------------------------------------------
if (-not $SkipDocker) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw "docker not found on PATH." }
    if ($Reset) {
        Warn "Reset requested -> 'docker compose down -v' (this DROPS the database)."
        docker compose -f (Join-Path $Root 'docker-compose.yml') down -v
    }
    Info "Starting Docker services (postgres, minio) ..."
    docker compose -f (Join-Path $Root 'docker-compose.yml') up -d
    Wait-Tcp 5432 'Postgres' 90 | Out-Null
    Wait-Tcp 9000 'MinIO'    60 | Out-Null
} else {
    Warn "SkipDocker -> assuming Postgres + MinIO are already running."
}

# ----------------------------------------------------------------------
# 2) .NET API (migrations auto-apply on startup; HuggingFace AI from appsettings)
# ----------------------------------------------------------------------
if (Test-Listening 5000) {
    Ok "API already running on http://localhost:5000 (skipping start)."
} else {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet not found on PATH." }
    Info "Starting .NET API (dotnet run, profile 'http') ..."
    Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run','--launch-profile','http' `
        -WorkingDirectory $ApiDir `
        -RedirectStandardOutput (Join-Path $LogDir 'api.out.log') `
        -RedirectStandardError  (Join-Path $LogDir 'api.err.log') `
        -WindowStyle Hidden | Out-Null
    Wait-Http 'http://localhost:5000/swagger/index.html' 'API (Swagger)' 150 | Out-Null
}

# ----------------------------------------------------------------------
# 3) Next.js frontend
# ----------------------------------------------------------------------
if (-not (Test-Path (Join-Path $WebDir '.env.local'))) {
    Warn "web\.env.local is MISSING -> API base URL + Auth0 will not be configured. See references/troubleshooting.md."
}
if (Test-Listening 3000) {
    Ok "Frontend already running on http://localhost:3000 (skipping start)."
} else {
    if (-not (Test-Path (Join-Path $WebDir 'node_modules'))) {
        Info "node_modules missing -> running 'npm install' (one-time) ..."
        Push-Location $WebDir
        try { npm install } finally { Pop-Location }
    }
    Info "Starting Next.js frontend (npm run dev) ..."
    Start-Process -FilePath 'npm.cmd' `
        -ArgumentList 'run','dev' `
        -WorkingDirectory $WebDir `
        -RedirectStandardOutput (Join-Path $LogDir 'web.out.log') `
        -RedirectStandardError  (Join-Path $LogDir 'web.err.log') `
        -WindowStyle Hidden | Out-Null
    Wait-Http 'http://localhost:3000' 'Frontend' 150 | Out-Null
}

# ----------------------------------------------------------------------
# Summary
# ----------------------------------------------------------------------
Write-Host ""
Write-Host "==================== STACK READY ====================" -ForegroundColor Green
Write-Host "  Frontend      : http://localhost:3000"
Write-Host "  API (Swagger) : http://localhost:5000/swagger"
Write-Host "  Hangfire      : http://localhost:5000/hangfire"
Write-Host "  MinIO console : http://localhost:9001  (minioadmin / minioadmin)"
Write-Host "  Postgres      : localhost:5432  (clinic_user / clinic_password)"
Write-Host "  Logs          : $LogDir"
Write-Host "  Stop all      : .\scripts\stop.ps1"
Write-Host "=====================================================" -ForegroundColor Green
