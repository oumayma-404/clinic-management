; ============================================================================================
; Clinic Management — Server Installer (Phase 5 / S6)
;
; One-click stand-up of the whole Local/offline-LAN stack on a clinic PC:
;   - bundled PostgreSQL 16 (fresh cluster, auto-start service, clinic_management + clinic_user)
;   - the .NET API as an auto-start Windows service (Kestrel = sole LAN-facing HTTPS front door)
;   - the Next.js web bundle as a localhost-only Node service (reverse-proxied by Kestrel)
;   - Local config written on the target; signing key + HTTPS cert self-generated on first boot
;
; Dependency order: PostgreSQL -> Web (Node) -> API front door. Only the HTTPS port is opened on
; the LAN firewall; the Node web port and the API's plain-HTTP port stay loopback-only.
;
; R-1: committed-but-not-executed here. Build the payload with ..\publish-server.ps1 on an operator
; build machine, then compile this script with Inno Setup 6 (ISCC.exe). See ..\README.md.
; ============================================================================================

#define AppName        "Clinic Management Server"
#define AppVersion     "1.0.0"
#define AppPublisher   "Clinic Management"
#define ServiceApi     "ClinicManagementApi"
#define ServiceWeb     "ClinicManagementWeb"
#define ServiceDb      "ClinicManagementDb"
#define HttpPort       "5000"
#define HttpsPort      "5001"
#define WebPort        "3000"
#define DbPort         "5432"
#define DbName         "clinic_management"
#define DbUser         "clinic_user"

[Setup]
AppId={{7F3C1A90-5E2B-4D6A-9C11-CLINICSERVER01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Clinic Management
DefaultGroupName=Clinic Management
DisableProgramGroupPage=yes
OutputDir={#SourcePath}\..\build-output
OutputBaseFilename=ClinicManagementServerSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
; Config, storage, logs and the DB cluster all live under the install dir (resolved via
; AppContext.BaseDirectory by the API — R-6), so a service whose CWD is System32 still finds them.

[Dirs]
Name: "{app}\.local";   Permissions: service-modify
Name: "{app}\api\Files"; Permissions: service-modify
Name: "{app}\api\logs";  Permissions: service-modify
Name: "{app}\pgdata";    Permissions: service-modify
Name: "{commonappdata}\ClinicManagement"

[Files]
; Payloads staged by ..\publish-server.ps1 into build-output\server\.
Source: "{#SourcePath}\..\build-output\server\api\*";      DestDir: "{app}\api";      Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#SourcePath}\..\build-output\server\web\*";      DestDir: "{app}\web";      Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#SourcePath}\..\build-output\server\node\*";     DestDir: "{app}\node";     Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#SourcePath}\..\build-output\server\postgres\*"; DestDir: "{app}\postgres"; Flags: recursesubdirs createallsubdirs ignoreversion
; NSSM (Non-Sucking Service Manager) hosts the Node web server as a Windows service (R-8).
; Operator drops nssm.exe into packaging\server\tools\ before compiling; optional at compile time.
Source: "{#SourcePath}\tools\nssm.exe"; DestDir: "{app}\tools"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\Clinic Management (serveur — localhost)"; Filename: "https://localhost:{#HttpsPort}"
Name: "{group}\Uninstall Clinic Management Server"; Filename: "{uninstallexe}"

[UninstallRun]
; Stop + remove services on uninstall (best-effort; ignore errors if already gone).
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceApi}"; Flags: runhidden; RunOnceId: "StopApi"
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceWeb}"; Flags: runhidden; RunOnceId: "StopWeb"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceApi}"; Flags: runhidden; RunOnceId: "DelApi"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceWeb}"; Flags: runhidden; RunOnceId: "DelWeb"
Filename: "{app}\postgres\bin\pg_ctl.exe"; Parameters: "unregister -N ""{#ServiceDb}"""; Flags: runhidden skipifdoesntexist; RunOnceId: "DelDb"
; Remove the LAN firewall hole opened by OpenFirewall — otherwise it persists after uninstall (Finding 4).
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Clinic Management HTTPS"""; Flags: runhidden; RunOnceId: "DelFwRule"

[Code]
{ SW_HIDE is a built-in Inno Setup constant — do not redeclare it (duplicate-identifier compile error). }

var
  DbPassword: string;       { clinic_user login password (also baked into the connection string) }
  PgSuperPassword: string;   { postgres superuser password (scram-sha-256, Finding 10) }

{ OS CSPRNG (bcrypt.dll) — replaces Inno's non-cryptographic, unseeded Random for generated secrets
  (Finding 12). BCRYPT_USE_SYSTEM_PREFERRED_RNG = 2; hAlgorithm = NULL (0). Returns STATUS_SUCCESS (0). }
function BCryptGenRandom(hAlgorithm: Cardinal; pbBuffer: AnsiString; cbBuffer: Cardinal; dwFlags: Cardinal): Integer;
  external 'BCryptGenRandom@bcrypt.dll stdcall';

{ Run a program hidden and wait; returns True on exit code 0. }
function RunWait(const FileName, Params, WorkingDir: string; var ResultCode: Integer): Boolean;
begin
  Result := Exec(FileName, Params, WorkingDir, SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

{ Cryptographically-random 24-char password over an unambiguous alphabet. Sourced from the OS CSPRNG so it
  is genuinely per-install-unique (Finding 12); enforced by scram-sha-256 auth (Finding 10). If the CSPRNG
  call fails (never expected on supported Windows), fail the install loudly rather than fall back to a weak,
  predictable password — a decorative-but-insecure secret is worse than a hard stop. (Inno's Pascal has no
  `Randomize`, and its `Random` is unseeded/deterministic, so there is no safe non-CSPRNG fallback.) }
function NewRandomPassword: string;
var
  Buf: AnsiString;
  I: Integer;
  Chars: string;
begin
  Chars := 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789';
  Result := '';
  SetLength(Buf, 24);
  if BCryptGenRandom(0, Buf, 24, 2) <> 0 then
    RaiseException('Impossible de générer un mot de passe sécurisé (BCryptGenRandom a échoué).');
  for I := 1 to 24 do
    Result := Result + Copy(Chars, (Ord(Buf[I]) mod Length(Chars)) + 1, 1);
end;

{ Write the machine-specific Local runtime config as appsettings.Production.json (the API service runs
  in the Production environment). No real secrets here — they were scrubbed from the bundled
  appsettings.json by publish-server.ps1; the signing key + HTTPS cert are self-generated on first boot. }
procedure WriteProductionConfig;
var
  Cfg, PgDump, Files, ConnStr, AppDir: string;
begin
  AppDir := ExpandConstant('{app}');
  PgDump := AppDir + '\postgres\bin\pg_dump.exe';
  Files  := AppDir + '\api\Files';
  ConnStr := 'Host=localhost;Port={#DbPort};Database={#DbName};Username={#DbUser};Password=' + DbPassword;

  { Escape backslashes for JSON. }
  StringChangeEx(PgDump, '\', '\\', True);
  StringChangeEx(Files, '\', '\\', True);
  StringChangeEx(ConnStr, '\', '\\', True);

  Cfg :=
    '{' + #13#10 +
    '  "Auth": { "Mode": "Local" },' + #13#10 +
    '  "ConnectionStrings": { "DefaultConnection": "' + ConnStr + '" },' + #13#10 +
    '  "FileStorage": { "BasePath": "' + Files + '" },' + #13#10 +
    '  "Backup": { "PgDumpPath": "' + PgDump + '", "DefaultDestination": "", "TimeoutSeconds": 1800 },' + #13#10 +
    '  "Hosting": { "HttpPort": {#HttpPort}, "HttpsPort": {#HttpsPort}, "WebPort": {#WebPort} },' + #13#10 +
    '  "Https": { "CertPath": "" }' + #13#10 +
    '}' + #13#10;

  SaveStringToFile(AppDir + '\api\appsettings.Production.json', Cfg, False);
end;

{ initdb a fresh cluster (password-enforced), register + start the PostgreSQL service, then create the DB +
  role. Returns False on ANY hard failure so the caller aborts instead of proceeding against a missing
  role/DB and reporting "success" (Finding 5). Auth is scram-sha-256, not trust, so no local OS account can
  connect password-less (Finding 10); psql authenticates via a temporary pgpass.conf that is deleted after
  bootstrap. }
function SetupPostgres: Boolean;
var
  Rc: Integer;
  PgBin, PgData, Psql, InitDb, PgCtl, PwFile, PgPassDir, PgPassFile, SqlFile, Sql, InitLog, CmdLine: string;
  LogText: AnsiString;
begin
  Result := False;
  PgBin  := ExpandConstant('{app}\postgres\bin');
  PgData := ExpandConstant('{app}\pgdata');
  InitDb := PgBin + '\initdb.exe';
  PgCtl  := PgBin + '\pg_ctl.exe';
  Psql   := PgBin + '\psql.exe';

  { Fresh cluster only if pgdata is not already a valid cluster. Enforce password auth and set the
    postgres superuser password via a temp --pwfile (deleted immediately after). }
  if not FileExists(PgData + '\PG_VERSION') then
  begin
    { initdb DROPS administrator privileges (PostgreSQL security) and then runs as the de-privileged
      interactive user, which cannot create or write a folder under Program Files. So the elevated
      installer must provide an EMPTY data dir and grant the accounts that need it Full Control:
        S-1-5-32-545 = BUILTIN\Users   (the de-privileged initdb user)
        S-1-5-18     = LocalSystem      (the DB service's default account)
        S-1-5-20     = NetworkService   (in case the service runs under it)
      A previous aborted install may have left a partial cluster — clear its CONTENTS but keep the dir. }
    if DirExists(PgData) then
      DelTree(PgData + '\*', False, True, True)
    else
      CreateDir(PgData);
    Exec(ExpandConstant('{sys}\icacls.exe'),
      '"' + PgData + '" /grant "*S-1-5-32-545:(OI)(CI)F" /grant "*S-1-5-18:(OI)(CI)F" /grant "*S-1-5-20:(OI)(CI)F"',
      '', SW_HIDE, ewWaitUntilTerminated, Rc);

    PwFile  := ExpandConstant('{tmp}\pg-super.pw');
    InitLog := ExpandConstant('{app}\initdb.log');
    SaveStringToFile(PwFile, PgSuperPassword, False);

    { Run via cmd.exe so initdb's stdout+stderr are CAPTURED to a log (Finding 5 — surface the real
      reason, not just an exit code). The nested quoting needs the outer "" cmd /C wrapper. }
    CmdLine := '/C ""' + InitDb + '" -D "' + PgData + '" -U postgres -A scram-sha-256 --pwfile="' + PwFile +
               '" --encoding=UTF8 --locale=C > "' + InitLog + '" 2>&1"';
    Exec(ExpandConstant('{sys}\cmd.exe'), CmdLine, PgBin, SW_HIDE, ewWaitUntilTerminated, Rc);
    DeleteFile(PwFile);

    if Rc <> 0 then
    begin
      LogText := '';
      LoadStringFromFile(InitLog, LogText);
      MsgBox('Échec de l''initialisation de PostgreSQL (initdb, code ' + IntToStr(Rc) + ').' + #13#10#13#10 +
             'Détail :' + #13#10 + LogText + #13#10#13#10 +
             'Journal complet : ' + InitLog + #13#10 + 'Installation interrompue.', mbError, MB_OK);
      Exit;
    end;
  end;

  { Register as an auto-start service (bind loopback only — the DB is never LAN-facing). Tolerate
    "already registered"/"already running" on re-install; the readiness probe below is the real gate. }
  RunWait(PgCtl, 'register -N "{#ServiceDb}" -D "' + PgData + '" -S auto -o "-p {#DbPort} -h 127.0.0.1"', PgBin, Rc);
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceDb}', '', SW_HIDE, ewWaitUntilTerminated, Rc);

  { Wait for readiness — hard-fail if the server never comes up. }
  if not RunWait(PgBin + '\pg_isready.exe', '-h 127.0.0.1 -p {#DbPort} -t 30', PgBin, Rc) then
  begin
    MsgBox('PostgreSQL ne répond pas (pg_isready). Installation interrompue.', mbError, MB_OK);
    Exit;
  end;

  { psql now needs a password (scram). Provide it via the default pgpass.conf so nothing is passed on the
    command line; the file is deleted after bootstrap. The AppData\postgresql path belongs to the
    (elevated) installer account, which is also the account the psql child runs under, so psql finds it. }
  PgPassDir  := ExpandConstant('{userappdata}\postgresql');
  PgPassFile := PgPassDir + '\pgpass.conf';
  ForceDirectories(PgPassDir);
  SaveStringToFile(PgPassFile,
    '127.0.0.1:{#DbPort}:*:postgres:' + PgSuperPassword + #13#10 +
    '127.0.0.1:{#DbPort}:*:{#DbUser}:' + DbPassword + #13#10, False);

  { Create the role if absent, then the database if absent (\gexec creates only when the guard returns a
    row). One script, ON_ERROR_STOP=1 so a genuine failure aborts the install. -w never prompts. }
  Sql :=
    'DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname=''{#DbUser}'') THEN ' +
    'CREATE ROLE {#DbUser} LOGIN PASSWORD ''' + DbPassword + '''; END IF; END $$;' + #13#10 +
    'SELECT ''CREATE DATABASE {#DbName} OWNER {#DbUser}'' ' +
    'WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname=''{#DbName}'')\gexec' + #13#10;
  SqlFile := ExpandConstant('{tmp}\clinic-db-init.sql');
  SaveStringToFile(SqlFile, Sql, False);

  if not RunWait(Psql, '-h 127.0.0.1 -p {#DbPort} -U postgres -d postgres -w -v ON_ERROR_STOP=1 -f "' + SqlFile + '"', PgBin, Rc) then
  begin
    DeleteFile(SqlFile);
    DeleteFile(PgPassFile);
    MsgBox('Échec de la création du rôle/de la base PostgreSQL (code ' + IntToStr(Rc) + '). Installation interrompue.', mbError, MB_OK);
    Exit;
  end;

  DeleteFile(SqlFile);
  DeleteFile(PgPassFile);   { remove the superuser secret from disk once bootstrap is done }
  Result := True;
end;

{ Register the Node web server (via NSSM) and the API (self-contained exe + UseWindowsService).
  The web service is created FIRST so the API's dependency on it is satisfiable, and the API depends on the
  web service ONLY when it was actually registered (Finding 6 — otherwise sc start fails with 1068 and the
  API is dead while the installer reports success). On upgrade, existing services are removed first so a
  changed binPath/env is re-applied rather than silently keeping the old definition (Finding 15). }
procedure SetupAppServices;
var
  Rc: Integer;
  ApiExe, NodeExe, ServerJs, WebDir, Nssm, ApiDepend: string;
  WebRegistered: Boolean;
begin
  ApiExe   := ExpandConstant('{app}\api\ClinicManagement.API.exe');
  NodeExe  := ExpandConstant('{app}\node\node.exe');
  WebDir   := ExpandConstant('{app}\web');
  ServerJs := WebDir + '\server.js';
  Nssm     := ExpandConstant('{app}\tools\nssm.exe');

  { --- Idempotent upgrade: tear down any existing services first (best-effort; ignore "not found"). --- }
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceApi}', '', SW_HIDE, ewWaitUntilTerminated, Rc);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceApi}', '', SW_HIDE, ewWaitUntilTerminated, Rc);
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceWeb}', '', SW_HIDE, ewWaitUntilTerminated, Rc);
  if FileExists(Nssm) then
    RunWait(Nssm, 'remove {#ServiceWeb} confirm', '', Rc)
  else
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceWeb}', '', SW_HIDE, ewWaitUntilTerminated, Rc);

  { --- Web service FIRST (via NSSM) — Node listens HTTP on localhost:{#WebPort} only (HOSTNAME=127.0.0.1). --- }
  WebRegistered := False;
  if FileExists(Nssm) then
  begin
    { Pass server.js RELATIVE (resolved against AppDirectory below), NOT as a full path: NSSM does not
      quote AppParameters, so a full path containing a space ("C:\Program Files\...") is split and Node
      receives "C:\Program" as its script → MODULE_NOT_FOUND. A relative "server.js" has no space. }
    RunWait(Nssm, 'install {#ServiceWeb} "' + NodeExe + '" server.js', '', Rc);
    RunWait(Nssm, 'set {#ServiceWeb} AppDirectory "' + WebDir + '"', '', Rc);
    RunWait(Nssm, 'set {#ServiceWeb} Start SERVICE_AUTO_START', '', Rc);
    RunWait(Nssm, 'set {#ServiceWeb} DependOnService {#ServiceDb}', '', Rc);
    { Same-origin build env for the co-located Next server (localhost only, never LAN-facing).
      AUTH_COOKIE_SECURE=true: the browser reaches the app over the HTTPS front door, but this Node
      server sits behind it on a plain-HTTP loopback hop, so the BFF login handler would otherwise
      derive a non-Secure request scheme and drop the Secure flag on the auth session cookie. Force it
      on — the front door is the TLS-terminating proxy the handler's override was written for. }
    RunWait(Nssm, 'set {#ServiceWeb} AppEnvironmentExtra ' +
      'PORT={#WebPort} HOSTNAME=127.0.0.1 NODE_ENV=production AUTH_MODE=local AUTH_COOKIE_SECURE=true ' +
      'NEXT_PUBLIC_API_URL=/api API_INTERNAL_URL=http://localhost:{#HttpPort}/api', '', Rc);
    WebRegistered := True;
  end
  else
    MsgBox('nssm.exe introuvable ({app}\tools\nssm.exe) — le service web n''a pas été enregistré. ' +
           'Ajoutez nssm.exe et réexécutez, ou enregistrez le service Node manuellement (voir README).',
           mbError, MB_OK);

  { --- API service — depend on the web server ONLY if it was actually created. --- }
  if WebRegistered then
    ApiDepend := '{#ServiceDb}/{#ServiceWeb}'
  else
    ApiDepend := '{#ServiceDb}';
  Exec(ExpandConstant('{sys}\sc.exe'),
    'create {#ServiceApi} binPath= "' + ApiExe + '" start= auto DisplayName= "Clinic Management API" depend= ' + ApiDepend,
    '', SW_HIDE, ewWaitUntilTerminated, Rc);
  Exec(ExpandConstant('{sys}\sc.exe'), 'failure {#ServiceApi} reset= 60 actions= restart/5000', '', SW_HIDE, ewWaitUntilTerminated, Rc);
end;

{ Open only the HTTPS front-door port on the LAN firewall. }
procedure OpenFirewall;
var
  Rc: Integer;
begin
  Exec(ExpandConstant('{sys}\netsh.exe'),
    'advfirewall firewall add rule name="Clinic Management HTTPS" dir=in action=allow protocol=TCP localport={#HttpsPort}',
    '', SW_HIDE, ewWaitUntilTerminated, Rc);
end;

{ Start web then API; wait for the API to self-generate its cert, then export the CA for clients. }
procedure StartAndExportCa;
var
  Rc, Tries: Integer;
  CaSrc, CaDst: string;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceWeb}', '', SW_HIDE, ewWaitUntilTerminated, Rc);
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceApi}', '', SW_HIDE, ewWaitUntilTerminated, Rc);

  CaSrc := ExpandConstant('{app}\.local\ca.crt');
  CaDst := ExpandConstant('{commonappdata}\ClinicManagement\ca.crt');

  { The CA is generated on first API boot; poll briefly for it. }
  Tries := 0;
  while (Tries < 30) and (not FileExists(CaSrc)) do
  begin
    Sleep(1000);
    Tries := Tries + 1;
  end;

  if FileExists(CaSrc) then
    FileCopy(CaSrc, CaDst, False)
  else
    MsgBox('Le certificat CA n''est pas encore généré. Une fois le service API démarré, copiez ' +
           '{app}\.local\ca.crt vers un support partagé pour l''installateur client (voir README).',
           mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    DbPassword      := NewRandomPassword;
    PgSuperPassword := NewRandomPassword;
  end;

  if CurStep = ssPostInstall then
  begin
    WriteProductionConfig;
    if SetupPostgres then
    begin
      SetupAppServices;
      OpenFirewall;
      StartAndExportCa;
    end;
  end;
end;
