#requires -Version 5.1
<#
.SYNOPSIS
    Downloads the two build-time tools the installers need but that aren't installed on this machine:
    PostgreSQL 16 Windows binaries (to bundle in the server installer) and NSSM (to host the web
    service). No installation, no admin, no services created — just download + extract into
    packaging\build-tools\ (gitignored) and drop nssm.exe where clinic-server.iss expects it.

    Inno Setup 6 is NOT fetched here (it needs an elevated install): run
    `winget install JRSoftware.InnoSetup` in an admin terminal separately.

.EXAMPLE
    .\fetch-build-tools.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'   # much faster Invoke-WebRequest downloads

$PackagingDir = $PSScriptRoot
$ToolsDir     = Join-Path $PackagingDir 'build-tools'
$ServerTools  = Join-Path $PackagingDir 'server\tools'
New-Item -ItemType Directory -Path $ToolsDir   -Force | Out-Null
New-Item -ItemType Directory -Path $ServerTools -Force | Out-Null

function Write-Step([string]$m) { Write-Host ''; Write-Host "==> $m" -ForegroundColor Cyan }

# --- PostgreSQL 16 Windows binaries (EnterpriseDB "binaries only" zip) --------------------------
Write-Step 'PostgreSQL 16 binaries'
$PgDir = Join-Path $ToolsDir 'pgsql'
if (Test-Path (Join-Path $PgDir 'bin\pg_dump.exe')) {
    Write-Host "Already present: $PgDir"
} else {
    # EDB publishes version-specific direct zips; try recent 16.x builds until one responds.
    $candidates = @('16.9-1','16.8-1','16.6-1','16.4-1','16.3-1')
    $ok = $false
    foreach ($v in $candidates) {
        $url = "https://get.enterprisedb.com/postgresql/postgresql-$v-windows-x64-binaries.zip"
        $zip = Join-Path $ToolsDir "postgresql-$v.zip"
        try {
            Write-Host "Trying $url"
            Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
            Write-Host "Extracting..."
            Expand-Archive -Path $zip -DestinationPath $ToolsDir -Force   # yields $ToolsDir\pgsql\
            Remove-Item $zip -Force
            $ok = $true
            break
        } catch {
            Write-Host "  not available ($($_.Exception.Message))"
            if (Test-Path $zip) { Remove-Item $zip -Force }
        }
    }
    if (-not $ok) {
        Write-Warning @"
Could not auto-download PostgreSQL 16 binaries. Download the 'Windows x86-64 binaries' zip manually from
https://www.enterprisedb.com/download-postgresql-binaries (PostgreSQL 16), extract it, and note the path to
the extracted 'pgsql' folder (the one containing bin\pg_dump.exe) — pass it to publish-server.ps1 as -PostgresDir.
"@
    }
}

# --- NSSM (Non-Sucking Service Manager) ---------------------------------------------------------
Write-Step 'NSSM'
$NssmDst = Join-Path $ServerTools 'nssm.exe'
if (Test-Path $NssmDst) {
    Write-Host "Already present: $NssmDst"
} else {
    $url = 'https://nssm.cc/release/nssm-2.24.zip'
    $zip = Join-Path $ToolsDir 'nssm-2.24.zip'
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $ToolsDir -Force
    Copy-Item (Join-Path $ToolsDir 'nssm-2.24\win64\nssm.exe') $NssmDst -Force
    Remove-Item $zip -Force
    Write-Host "Placed nssm.exe -> $NssmDst"
}

# --- Report -------------------------------------------------------------------------------------
Write-Step 'Summary'
$pgOk   = Test-Path (Join-Path $PgDir 'bin\pg_dump.exe')
$nssmOk = Test-Path $NssmDst
Write-Host ("PostgreSQL binaries : {0}" -f $(if ($pgOk)   { $PgDir } else { 'MISSING (see warning above)' }))
Write-Host ("NSSM                : {0}" -f $(if ($nssmOk) { $NssmDst } else { 'MISSING' }))
Write-Host ("Node (for -NodeDir) : {0}" -f (Split-Path (Get-Command node).Source))
$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if (-not $iscc -and (Test-Path 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe')) { $iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' }
Write-Host ("Inno Setup (ISCC)   : {0}" -f $(if ($iscc) { 'installed' } else { 'MISSING -> run: winget install JRSoftware.InnoSetup (admin)' }))
Write-Host ''
if ($pgOk -and $nssmOk) {
    Write-Host 'Ready. Next: build the installers with' -ForegroundColor Green
    Write-Host ("  .\publish-server.ps1 -PostgresDir `"$PgDir`" -NodeDir `"$(Split-Path (Get-Command node).Source)`"")
}
