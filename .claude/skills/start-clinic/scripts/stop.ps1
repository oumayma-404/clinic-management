#requires -Version 5.1
<#
.SYNOPSIS
    Stop the clinic-management stack started by start.ps1.
.DESCRIPTION
    Stops the .NET API (public + console listeners), both Next.js dev servers
    (the clinic app on 3000 and the vendor console on 3100), then stops
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
# Pinned for start.ps1's reason: a worktree's directory name would otherwise address a
# different compose project, and this would stop nothing.
$ComposeProject = 'clinic-management'

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

Stop-Port 3100 'Vendor console (Next.js)'
Stop-Port 3000 'Frontend (Next.js)'
# One process serves both the public API and the console listener on 5443, so stopping 5000
# takes 5443 with it; the second call is the belt-and-braces case where only 5443 survived.
Stop-Port 5000 'API (.NET)'
Stop-Port 5443 'API console listener (.NET)'

if (-not $KeepDocker) {
    Write-Host "[stop] Stopping Docker services ..." -ForegroundColor Yellow
    if ($Reset) { docker compose -p $ComposeProject -f (Join-Path $Root 'docker-compose.yml') down -v }
    else        { docker compose -p $ComposeProject -f (Join-Path $Root 'docker-compose.yml') down }
}
Write-Host "[stop] Done." -ForegroundColor Green
