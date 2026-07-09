#requires -Version 5.1
<#
.SYNOPSIS
    Phase 5 (S6/S7) build & publish orchestration for the Local / offline-LAN Windows install.

.DESCRIPTION
    Stages everything the Inno Setup installers (server/clinic-server.iss, client/clinic-client.iss)
    bundle, into packaging/build-output/ (gitignored):

      build-output/server/
        api/        self-contained win-x64 publish of ClinicManagement.API (+ Windows-service exe)
        web/        Next.js standalone output (server.js + .next/static + public) -- HTTP on localhost only
        node/       Node.js runtime (node.exe) that runs the standalone web server
        postgres/   EnterpriseDB PostgreSQL 16 Windows binaries (initdb/pg_ctl/pg_dump/pg_restore)
      build-output/client/
        shell/      self-contained win-x64 publish of ClinicManagement.DesktopShell (WebView2 client)

    This script does NOT fabricate the third-party runtimes. Point -PostgresDir and -NodeDir at local
    copies of EDB PostgreSQL 16 and a Node.js runtime; the script copies them into the staging tree.

    NOTE (R-1): this environment cannot be relied on to produce/execute the final installer. The script
    is a committed, reviewable build recipe; run it on an operator build machine with the .NET 8 SDK,
    Node.js, Inno Setup, and the two runtimes available.

.PARAMETER PostgresDir
    Path to an extracted EnterpriseDB PostgreSQL 16 Windows distribution (the folder containing bin\, lib\,
    share\). Required for the server bundle (bundled DB + pg_dump/pg_restore for backup/restore).

.PARAMETER NodeDir
    Path to a Node.js runtime folder containing node.exe. Required to host the standalone web server.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER SkipInstallers
    Stage the payloads but do not invoke Inno Setup (ISCC.exe).

.EXAMPLE
    .\publish-server.ps1 -PostgresDir C:\pgsql-16 -NodeDir C:\node-v20
#>
[CmdletBinding()]
param(
    [string]$PostgresDir,
    [string]$NodeDir,
    [string]$Configuration = 'Release',
    [switch]$SkipInstallers
)

$ErrorActionPreference = 'Stop'

# --- Paths -------------------------------------------------------------------------------------
$PackagingDir = $PSScriptRoot
$RepoRoot     = Split-Path $PackagingDir -Parent
$ApiProject   = Join-Path $RepoRoot 'api\ClinicManagement.API\ClinicManagement.API.csproj'
$WebDir       = Join-Path $RepoRoot 'web'
$ShellProject = Join-Path $RepoRoot 'desktop\ClinicManagement.DesktopShell\ClinicManagement.DesktopShell.csproj'

$OutputRoot   = Join-Path $PackagingDir 'build-output'
$ServerOut    = Join-Path $OutputRoot 'server'
$ClientOut    = Join-Path $OutputRoot 'client'

$Rid = 'win-x64'

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Clear-Dir([string]$Path) {
    if (Test-Path $Path) { Remove-Item $Path -Recurse -Force }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

# --- 0. Preconditions --------------------------------------------------------------------------
Write-Step 'Checking build prerequisites'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet SDK not found on PATH.' }
if (-not (Get-Command npm    -ErrorAction SilentlyContinue)) { throw 'npm (Node.js) not found on PATH.' }

Clear-Dir $ServerOut
Clear-Dir $ClientOut

# --- 1. Publish the API (self-contained win-x64, Windows-service capable) -----------------------
Write-Step 'Publishing ClinicManagement.API (self-contained win-x64)'
$ApiOut = Join-Path $ServerOut 'api'
dotnet publish $ApiProject `
    -c $Configuration `
    -r $Rid `
    --self-contained true `
    /p:UseAppHost=true `
    -o $ApiOut
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (API) failed with exit code $LASTEXITCODE." }

# Scrub the real-looking secrets out of the PUBLISHED appsettings.json so no real secret is bundled
# (FR-F4). Operates on the publish OUTPUT only -- the committed source is untouched. Local runtime config
# (connection string with the generated DB password, ports, pg_dump path) is written on the target by
# the installer as appsettings.Production.json; the signing key + HTTPS cert are generated on first boot
# into .local/ by the API itself (S2/S3).
Write-Step 'Scrubbing bundled secrets from published appsettings.json (FR-F4)'
$ApiSettings = Join-Path $ApiOut 'appsettings.json'
if (Test-Path $ApiSettings) {
    $cfg = Get-Content $ApiSettings -Raw | ConvertFrom-Json
    if ($cfg.Auth) { $cfg.Auth.Mode = 'Local' }   # guarded like the others (Finding 13)
    if ($cfg.GoogleCalendar) {
        $cfg.GoogleCalendar.ClientId     = ''
        $cfg.GoogleCalendar.ClientSecret = ''
        $cfg.GoogleCalendar.RefreshToken = ''
    }
    if ($cfg.HuggingFace) { $cfg.HuggingFace.ApiKey = '' }
    if ($cfg.Auth0 -and $cfg.Auth0.ManagementApi) {
        $cfg.Auth0.ManagementApi.ClientId     = ''
        $cfg.Auth0.ManagementApi.ClientSecret = ''
    }
    # Blank the remaining bundled secrets so NO real secret ships (Finding 13). Low impact — MinIO is unused
    # in Local and the connection string is overridden by the installer's appsettings.Production.json — but
    # this keeps the "no secret shipped" guarantee honest.
    if ($cfg.MinIO) {
        $cfg.MinIO.AccessKey = ''
        $cfg.MinIO.SecretKey = ''
    }
    if ($cfg.ConnectionStrings -and $cfg.ConnectionStrings.DefaultConnection) {
        $cfg.ConnectionStrings.DefaultConnection = $cfg.ConnectionStrings.DefaultConnection -replace 'Password=[^;]*', 'Password='
    }
    # Write BOM-less UTF-8 (Finding 14): Set-Content -Encoding UTF8 on Windows PowerShell 5.1 emits a BOM,
    # which trips any consumer that reads appsettings.json as a string into System.Text.Json.
    [System.IO.File]::WriteAllText($ApiSettings, ($cfg | ConvertTo-Json -Depth 32), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Scrubbed: $ApiSettings"
} else {
    Write-Warning "Published appsettings.json not found at $ApiSettings -- cannot scrub secrets."
}

# --- 2. Build the web bundle (Next.js standalone, Local same-origin env) ------------------------
Write-Step 'Building web (Next.js standalone, NEXT_PUBLIC_API_URL=/api)'
Push-Location $WebDir
try {
    $env:NEXT_PUBLIC_API_URL = '/api'          # relative -> same-origin (Kestrel front door)
    $env:AUTH_MODE           = 'local'
    $env:API_INTERNAL_URL    = 'http://localhost:5000/api'  # server-only; BFF handlers call the API directly

    if (-not (Test-Path (Join-Path $WebDir 'node_modules'))) {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
    }

    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

# Assemble the standalone runtime tree the way `next start`-less hosting expects:
#   web/server.js + web/.next/static + web/public
Write-Step 'Staging Next.js standalone output'
$WebOut       = Join-Path $ServerOut 'web'
$StandaloneIn = Join-Path $WebDir '.next\standalone'
$StaticIn     = Join-Path $WebDir '.next\static'
$PublicIn     = Join-Path $WebDir 'public'
if (-not (Test-Path $StandaloneIn)) {
    throw "Next standalone output not found at $StandaloneIn. Ensure next.config has output: 'standalone'."
}
New-Item -ItemType Directory -Path $WebOut -Force | Out-Null
Copy-Item (Join-Path $StandaloneIn '*') $WebOut -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $WebOut '.next\static') -Force | Out-Null
Copy-Item (Join-Path $StaticIn '*') (Join-Path $WebOut '.next\static') -Recurse -Force
if (Test-Path $PublicIn) {
    Copy-Item $PublicIn (Join-Path $WebOut 'public') -Recurse -Force
}

# --- 3. Stage the Node.js runtime ---------------------------------------------------------------
Write-Step 'Staging Node.js runtime'
$NodeOut = Join-Path $ServerOut 'node'
if ($NodeDir -and (Test-Path (Join-Path $NodeDir 'node.exe'))) {
    New-Item -ItemType Directory -Path $NodeOut -Force | Out-Null
    Copy-Item (Join-Path $NodeDir '*') $NodeOut -Recurse -Force
} else {
    Write-Warning "NodeDir not supplied or node.exe missing -- server/node/ left empty. Provide -NodeDir on the build machine."
}

# --- 4. Stage bundled PostgreSQL 16 -------------------------------------------------------------
Write-Step 'Staging PostgreSQL 16 binaries'
$PgOut = Join-Path $ServerOut 'postgres'
if ($PostgresDir -and (Test-Path (Join-Path $PostgresDir 'bin\pg_dump.exe'))) {
    New-Item -ItemType Directory -Path $PgOut -Force | Out-Null
    Copy-Item (Join-Path $PostgresDir '*') $PgOut -Recurse -Force
} else {
    Write-Warning "PostgresDir not supplied or bin\pg_dump.exe missing -- server/postgres/ left empty. Provide -PostgresDir on the build machine."
}

# --- 5. Publish the WebView2 desktop shell (client) ---------------------------------------------
Write-Step 'Publishing ClinicManagement.DesktopShell (self-contained win-x64)'
$ShellOut = Join-Path $ClientOut 'shell'
dotnet publish $ShellProject `
    -c $Configuration `
    -r $Rid `
    --self-contained true `
    /p:UseAppHost=true `
    -o $ShellOut
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (DesktopShell) failed with exit code $LASTEXITCODE." }

# The client installer imports the server CA; the operator drops ca.crt here (exported by the server, S6).
New-Item -ItemType Directory -Path (Join-Path $ClientOut 'ca') -Force | Out-Null

# WebView2 Evergreen runtime (Finding 16): on an offline LAN PC without the runtime the shell can't render
# and there is no internet to fetch a bootstrapper. Drop the OFFLINE standalone installer
# (MicrosoftEdgeWebView2Setup.exe, "Evergreen Standalone Installer") here on the build machine; the client
# installer bundles it (optional) and runs it silently only when the runtime is missing.
New-Item -ItemType Directory -Path (Join-Path $ClientOut 'webview2') -Force | Out-Null
Write-Host "Drop the offline WebView2 runtime installer into: $(Join-Path $ClientOut 'webview2')\MicrosoftEdgeWebView2Setup.exe"

# --- 6. Compile the installers (optional) -------------------------------------------------------
if (-not $SkipInstallers) {
    Write-Step 'Compiling installers (Inno Setup)'
    $Iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if (-not $Iscc) {
        $default = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
        if (Test-Path $default) { $Iscc = $default } else { $Iscc = $null }
    }
    if ($Iscc) {
        & $Iscc (Join-Path $PackagingDir 'server\clinic-server.iss')
        if ($LASTEXITCODE -ne 0) { throw "ISCC (server) failed with exit code $LASTEXITCODE." }
        & $Iscc (Join-Path $PackagingDir 'client\clinic-client.iss')
        if ($LASTEXITCODE -ne 0) { throw "ISCC (client) failed with exit code $LASTEXITCODE." }
    } else {
        Write-Warning 'ISCC.exe (Inno Setup 6) not found -- skipping installer compilation. Payloads are staged under build-output/.'
    }
}

Write-Step 'Done'
Write-Host "Server payload: $ServerOut"
Write-Host "Client payload: $ClientOut"
