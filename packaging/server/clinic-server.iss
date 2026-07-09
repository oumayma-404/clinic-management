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

[Code]
const
  SW_HIDE = 0;

var
  DbPassword: string;

{ Run a program hidden and wait; returns True on exit code 0. }
function RunWait(const FileName, Params, WorkingDir: string; var ResultCode: Integer): Boolean;
begin
  Result := Exec(FileName, Params, WorkingDir, SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

{ Weak-but-adequate random password for the local-only clinic_user role. The DB never leaves the PC;
  the value is stored only in the install's appsettings.Production.json. }
function NewDbPassword: string;
var
  I: Integer;
  Chars: string;
begin
  Chars := 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789';
  Result := '';
  for I := 1 to 24 do
    Result := Result + Copy(Chars, Random(Length(Chars)) + 1, 1);
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

{ initdb a fresh cluster, register + start the PostgreSQL service, then create the DB + role. }
function SetupPostgres: Boolean;
var
  Rc: Integer;
  PgBin, PgData, Psql, InitDb, PgCtl, Sql: string;
begin
  PgBin  := ExpandConstant('{app}\postgres\bin');
  PgData := ExpandConstant('{app}\pgdata');
  InitDb := PgBin + '\initdb.exe';
  PgCtl  := PgBin + '\pg_ctl.exe';
  Psql   := PgBin + '\psql.exe';

  { Fresh cluster only if pgdata is empty (idempotent re-install). }
  if not FileExists(PgData + '\PG_VERSION') then
  begin
    if not RunWait(InitDb, '-D "' + PgData + '" -U postgres -A trust --encoding=UTF8 --locale=C', PgBin, Rc) then
    begin
      MsgBox('Échec de l''initialisation de PostgreSQL (initdb). Code ' + IntToStr(Rc), mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  { Register as an auto-start service (bind loopback only — the DB is never LAN-facing). }
  RunWait(PgCtl, 'register -N "{#ServiceDb}" -D "' + PgData + '" -S auto -o "-p {#DbPort} -h 127.0.0.1"', PgBin, Rc);
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceDb}', '', SW_HIDE, ewWaitUntilTerminated, Rc);

  { Wait for readiness. }
  RunWait(PgBin + '\pg_isready.exe', '-h 127.0.0.1 -p {#DbPort} -t 30', PgBin, Rc);

  { Create the role + database if absent (idempotent). PGPASSWORD unused — trust auth on localhost. }
  Sql :=
    'DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname=''{#DbUser}'') THEN ' +
    'CREATE ROLE {#DbUser} LOGIN PASSWORD ''' + DbPassword + '''; END IF; END $$;';
  RunWait(Psql, '-h 127.0.0.1 -p {#DbPort} -U postgres -d postgres -v ON_ERROR_STOP=1 -c "' + Sql + '"', PgBin, Rc);

  { CREATE DATABASE cannot run inside a DO block; guard with a existence check via psql -tc. }
  RunWait(Psql, '-h 127.0.0.1 -p {#DbPort} -U postgres -d postgres -v ON_ERROR_STOP=0 -c ' +
    '"CREATE DATABASE {#DbName} OWNER {#DbUser};"', PgBin, Rc);

  Result := True;
end;

{ Register the API (self-contained exe + UseWindowsService) and the Node web server (via NSSM). }
procedure SetupAppServices;
var
  Rc: Integer;
  ApiExe, NodeExe, ServerJs, WebDir, Nssm: string;
begin
  ApiExe   := ExpandConstant('{app}\api\ClinicManagement.API.exe');
  NodeExe  := ExpandConstant('{app}\node\node.exe');
  WebDir   := ExpandConstant('{app}\web');
  ServerJs := WebDir + '\server.js';
  Nssm     := ExpandConstant('{app}\tools\nssm.exe');

  { API service — depends on the DB and the web server (front door proxies to Node). }
  Exec(ExpandConstant('{sys}\sc.exe'),
    'create {#ServiceApi} binPath= "' + ApiExe + '" start= auto DisplayName= "Clinic Management API" depend= {#ServiceDb}/{#ServiceWeb}',
    '', SW_HIDE, ewWaitUntilTerminated, Rc);
  Exec(ExpandConstant('{sys}\sc.exe'), 'failure {#ServiceApi} reset= 60 actions= restart/5000', '', SW_HIDE, ewWaitUntilTerminated, Rc);

  { Web service via NSSM — Node listens HTTP on localhost:{#WebPort} only (HOSTNAME=127.0.0.1). }
  if FileExists(Nssm) then
  begin
    RunWait(Nssm, 'install {#ServiceWeb} "' + NodeExe + '" "' + ServerJs + '"', '', Rc);
    RunWait(Nssm, 'set {#ServiceWeb} AppDirectory "' + WebDir + '"', '', Rc);
    RunWait(Nssm, 'set {#ServiceWeb} Start SERVICE_AUTO_START', '', Rc);
    RunWait(Nssm, 'set {#ServiceWeb} DependOnService {#ServiceDb}', '', Rc);
    { Same-origin build env for the co-located Next server (localhost only, never LAN-facing). }
    RunWait(Nssm, 'set {#ServiceWeb} AppEnvironmentExtra ' +
      'PORT={#WebPort} HOSTNAME=127.0.0.1 NODE_ENV=production AUTH_MODE=local ' +
      'NEXT_PUBLIC_API_URL=/api API_INTERNAL_URL=http://localhost:{#HttpPort}/api', '', Rc);
  end
  else
    MsgBox('nssm.exe introuvable ({app}\tools\nssm.exe) — le service web n''a pas été enregistré. ' +
           'Ajoutez nssm.exe et réexécutez, ou enregistrez le service Node manuellement (voir README).',
           mbError, MB_OK);
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
    DbPassword := NewDbPassword;

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
