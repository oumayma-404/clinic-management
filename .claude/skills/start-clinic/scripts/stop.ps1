#requires -Version 5.1
<#
.SYNOPSIS
    Stop the clinic-management stack started by start.ps1.
.DESCRIPTION
    Stops the .NET API and Next.js dev server (by listening port), then stops
    the Docker services. Database data is preserved (volumes are NOT removed)
    unless -Reset is passed.
.PARAMETER KeepDocker
    Leave Postgres + MinIO running; only stop the API and frontend.
.PARAMETER Reset
    Also remove Docker volumes (DROPS the database).
#>
[CmdletBinding()]
param(
    [switch]$KeepDocker,
    [switch]$Reset
)

$ErrorActionPreference = 'Continue'

function Find-ProjectRoot {
    $dir = $PSScriptRoot
    for ($i = 0; $i -lt 8; $i++) {
        if (Test-Path (Join-Path $dir 'docker-compose.yml')) { return $dir }
        $dir = Split-Path -Parent $dir
    }
    throw "Could not find project root (docker-compose.yml) above $PSScriptRoot"
}
$Root = Find-ProjectRoot

function Stop-Port([int]$Port, [string]$Name) {
    try {
        $pids = (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue).OwningProcess | Sort-Object -Unique
        if (-not $pids) { Write-Host "[stop] $Name not running on port $Port"; return }
        foreach ($procId in $pids) {
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
            Write-Host "[stop] $Name (PID $procId, port $Port) stopped" -ForegroundColor Yellow
        }
    } catch { Write-Host "[stop] could not stop $Name on port ${Port}: $_" }
}

Stop-Port 3000 'Frontend (Next.js)'
Stop-Port 5000 'API (.NET)'

if (-not $KeepDocker) {
    Write-Host "[stop] Stopping Docker services ..." -ForegroundColor Yellow
    if ($Reset) { docker compose -f (Join-Path $Root 'docker-compose.yml') down -v }
    else        { docker compose -f (Join-Path $Root 'docker-compose.yml') down }
}
Write-Host "[stop] Done." -ForegroundColor Green
