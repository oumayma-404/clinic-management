#requires -Version 5.1
<#
.SYNOPSIS
    Launch the clinic-management stack locally, end to end.
.DESCRIPTION
    Brings everything up in dependency order and waits for each tier to be healthy:
      1. Docker services  -> Postgres (5432) + MinIO (9000/9001)
      2. .NET API         -> http://localhost:5000 (EF migrations auto-apply on startup; AI via HuggingFace)
                             plus the VENDOR CONSOLE listener on http://localhost:5443
      3. Next.js frontend -> http://localhost:3000
      4. Vendor console   -> http://localhost:3100 (the second Next app, `console/`)
    Idempotent: skips a tier that is already listening on its port.
    API and frontend are started as detached background processes; their output is
    redirected to log files (path printed at the end).
.PARAMETER SkipDocker
    Do not touch Docker (use when Postgres/MinIO are already running elsewhere).
.PARAMETER Reset
    Recreate Docker volumes (DROPS the database) before starting.
.PARAMETER ApiDebug
    Build/run the API in Debug instead of Release. Release is the default because Smart App
    Control on this machine intermittently refuses freshly-built DEBUG assemblies
    (FileLoadException 0x800711C7, « An Application Control policy has blocked this file »),
    which kills `dotnet run` before it binds a port. A Release build emits different bytes, so
    SAC judges a different file. Use -ApiDebug when you need to attach a debugger.
.PARAMETER SkipConsole
    Do not start the vendor console (`console/`) or bootstrap its account. The API still
    binds its listener on 5443, because that is decided by Console:Port in
    appsettings.Development.json, not by this script.
.NOTES
    The vendor console exists ONLY on Deployment:Profile = HostedMultiTenant, which local
    dev already uses. Its two config keys live in appsettings.Development.json; this script
    adds the second Next app, the first console account, and one run of the activity
    counter pass so the portfolio is not entirely « jamais mesuré ».
#>
[CmdletBinding()]
param(
    [switch]$SkipDocker,
    [switch]$Reset,
    [switch]$SkipConsole,
    [switch]$ApiDebug
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
$ConsoleDir = Join-Path $Root 'console'
$ConsolePort    = 3100
$ConsoleApiPort = 5443
$ApiConfig      = if ($ApiDebug) { 'Debug' } else { 'Release' }
# ⚠️ Pinned rather than derived from the directory name: run from a git WORKTREE and compose
# would otherwise invent a second project (`platform-console_*`) with its own empty volumes,
# and the script would then wait on a database that has none of your data in it.
$ComposeProject = 'clinic-management'
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
        docker compose -p $ComposeProject -f (Join-Path $Root 'docker-compose.yml') down -v
    }
    Info "Starting Docker services (postgres, minio) ..."
    docker compose -p $ComposeProject -f (Join-Path $Root 'docker-compose.yml') up -d
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
    Info "Starting .NET API (dotnet run -c $ApiConfig, profile 'http') ..."
    Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run','-c',$ApiConfig,'--launch-profile','http' `
        -WorkingDirectory $ApiDir `
        -RedirectStandardOutput (Join-Path $LogDir 'api.out.log') `
        -RedirectStandardError  (Join-Path $LogDir 'api.err.log') `
        -WindowStyle Hidden | Out-Null
    if (-not (Wait-Http 'http://localhost:5000/swagger/index.html' 'API (Swagger)' 180)) {
        $errLog = Join-Path $LogDir 'api.err.log'
        if ((Test-Path $errLog) -and (Select-String -Path $errLog -Pattern '0x800711C7' -Quiet)) {
            Warn "Smart App Control blocked the freshly-built API assembly (0x800711C7). Retry -- it is intermittent -- or, if you passed -ApiDebug, drop it: a Release build emits different bytes and SAC judges a different file."
        } else {
            Warn "See $errLog"
        }
    }
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
# 4) Vendor console (platform-console) -- the second Next app, plus its first account
# ----------------------------------------------------------------------
# The API's console listener is bound by Console:Port in appsettings.Development.json, not
# here, so 5443 is up already if the API is. This tier adds the browser app in front of it.
$ConsoleReady = $false
if ($SkipConsole) {
    Warn "SkipConsole -> the API still answers on $ConsoleApiPort; console/ not started."
} elseif (-not (Test-Path $ConsoleDir)) {
    Warn "console/ is absent from this checkout -> vendor console skipped (are you on a branch that has it?)."
} else {
    if (-not (Test-Listening $ConsoleApiPort)) {
        Warn "The API is not listening on $ConsoleApiPort. Console:Port is probably unset in appsettings.Development.json, or the API failed to start; console/ would have nothing to talk to."
    } else {
        # --- 4a) the first console account. There is NO sign-up screen: this verb is the only door
        #         (AC-8.1/8.5), and it prints a one-time password + a TOTP enrolment secret ONCE.
        $accountCount = $null
        try {
            $raw = docker exec clinic-postgres psql -U clinic_user -d clinic_management -tAc 'select count(*) from "PlatformAccounts"' 2>$null
            if ($LASTEXITCODE -eq 0) { $accountCount = [int]($raw | Select-Object -First 1).Trim() }
        } catch { }

        if ($null -eq $accountCount) {
            Warn "Could not count console accounts (is the postgres container named clinic-postgres?). Create one by hand if you have none:"
            Write-Host "        cd api\ClinicManagement.API; `$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run -c $ApiConfig --no-build --no-launch-profile -- platform-account create --email ops@editeur.tn --name `"Votre nom`"" -ForegroundColor DarkGray
        } elseif ($accountCount -gt 0) {
            Ok "$accountCount console account(s) already exist (nothing printed -- the password and secret are shown once, at creation)."
        } else {
            Info "No console account exists -> creating the first one. READ THE OUTPUT: the password and the enrolment secret are shown ONCE."
            $prevEnv = $env:ASPNETCORE_ENVIRONMENT
            $env:ASPNETCORE_ENVIRONMENT = 'Development'
            try {
                Push-Location $ApiDir
                # --no-build: the API is running and holds bin\Debug, so a build here dies on MSB3021.
                # It runs the binary the API tier just built, which is this checkout's code.
                & dotnet run -c $ApiConfig --no-build --no-launch-profile -- platform-account create --email ops@editeur.tn --name 'Operateur local'
            } finally {
                Pop-Location
                $env:ASPNETCORE_ENVIRONMENT = $prevEnv
            }
        }

        # --- 4b) one run of the activity counter pass. It is a DAILY job (03:00 UTC), so without
        #         this every cabinet reads « jamais mesuré » -- which is correct and useless.
        #         /hangfire is loopback-only in every profile, and here we ARE loopback.
        try {
            Invoke-WebRequest -Uri 'http://localhost:5000/hangfire/recurring/trigger' -Method Post `
                -Body 'jobs[]=count-clinic-activity' -ContentType 'application/x-www-form-urlencoded' `
                -UseBasicParsing -TimeoutSec 20 -ErrorAction Stop | Out-Null
            Ok "Triggered count-clinic-activity (the portfolio's figures; it also rewrites the last 30 days)."
        } catch {
            Warn "Could not trigger count-clinic-activity. Do it from http://localhost:5000/hangfire -> Recurring jobs -> Trigger now, or the portfolio will read « jamais mesuré » until 03:00 UTC."
        }

        # --- 4c) the app itself. CONSOLE_API_URL defaults to the docker service name, so it has to
        #         be set here; it is read SERVER-SIDE only (every console read is a server component,
        #         because the session cookie is HttpOnly).
        if (Test-Listening $ConsolePort) {
            Ok "Vendor console already running on http://localhost:$ConsolePort (skipping start)."
            $ConsoleReady = $true
        } else {
            if (-not (Test-Path (Join-Path $ConsoleDir 'node_modules'))) {
                Info "console\node_modules missing -> running 'npm install' (one-time) ..."
                Push-Location $ConsoleDir
                try { npm install } finally { Pop-Location }
            }
            Info "Starting the vendor console (npm run dev, port $ConsolePort) ..."
            $env:CONSOLE_API_URL = "http://localhost:$ConsoleApiPort/api"
            Start-Process -FilePath 'npm.cmd' `
                -ArgumentList 'run','dev','--','-p',"$ConsolePort" `
                -WorkingDirectory $ConsoleDir `
                -RedirectStandardOutput (Join-Path $LogDir 'console.out.log') `
                -RedirectStandardError  (Join-Path $LogDir 'console.err.log') `
                -WindowStyle Hidden | Out-Null
            $ConsoleReady = Wait-Http "http://localhost:$ConsolePort/login" 'Vendor console' 150
        }
    }
}

# ----------------------------------------------------------------------
# Summary
# ----------------------------------------------------------------------
Write-Host ""
Write-Host "==================== STACK READY ====================" -ForegroundColor Green
Write-Host "  Frontend      : http://localhost:3000"
Write-Host "  API (Swagger) : http://localhost:5000/swagger"
Write-Host "  Hangfire      : http://localhost:5000/hangfire"
if (-not $SkipConsole) {
    Write-Host "  Vendor console: http://localhost:$ConsolePort   (its API listener: $ConsoleApiPort)" -ForegroundColor Cyan
    if ($ConsoleReady) {
        Write-Host "                  Sign in needs a TOTP code: put the enrolment secret printed above into an" -ForegroundColor DarkGray
        Write-Host "                  authenticator app, then the login screen walks you through enrolment." -ForegroundColor DarkGray
    }
}
Write-Host "  MinIO console : http://localhost:9001  (minioadmin / minioadmin)"
Write-Host "  Postgres      : localhost:5432  (clinic_user / clinic_password)"
Write-Host "  Logs          : $LogDir"
Write-Host "  Stop all      : .\scripts\stop.ps1"
Write-Host "=====================================================" -ForegroundColor Green
