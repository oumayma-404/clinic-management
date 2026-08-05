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
; Cleartext LAN port serving ONLY the device-trust page. Must match Hosting:TrustPort in the API config
; and the API's TrustPortGate.DefaultPort -- the page prints and QR-encodes its own address, so a mismatch
; advertises a port nothing listens on.
#define TrustPort      "5080"
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
; Permissions are deliberately NOT set here any more. Inno's `Permissions:` flag only ADDS an ACE and leaves
; the inherited "Users: Read & Execute" from Program Files fully intact -- which is exactly the defect this
; release fixes (audit section 2, findings 2-3: the JWT signing key, the HTTPS server key, the Data
; Protection key ring, the e-invoice certificate and every uploaded radiograph were readable by every local
; account). The directories are created here and SECURED after install by the API's `harden-permissions`
; console verb, which breaks inheritance and removes Users/Everyone -- one testable implementation, shared
; with the one-click backup so the two cannot drift.
Name: "{app}\api\.local"
Name: "{app}\api\Files"
Name: "{app}\api\logs"
; L4b -- the real default backup destination. The config used to carry "" for it while the settings
; screen said "leave the field blank to use the server default folder", so the documented default path
; failed on every fresh install. Created here and hardened with the other data directories below.
Name: "{app}\api\Backups"
Name: "{app}\pgdata"
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
; Remove the LAN firewall holes opened by OpenFirewall — otherwise they persist after uninstall (Finding 4).
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Clinic Management HTTPS"""; Flags: runhidden; RunOnceId: "DelFwRule"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Clinic Management Trust"""; Flags: runhidden; RunOnceId: "DelFwRuleTrust"

[Code]
{ SW_HIDE is a built-in Inno Setup constant — do not redeclare it (duplicate-identifier compile error). }

var
  DbPassword: string;       { clinic_user login password (also baked into the connection string) }
  PgSuperPassword: string;   { postgres superuser password (scram-sha-256, Finding 10) }
  LastVerbOutput: AnsiString; { stdout+stderr of the most recent API console verb, for operator messages }

{ OS CSPRNG (bcrypt.dll) — replaces Inno's non-cryptographic, unseeded Random for generated secrets
  (Finding 12). BCRYPT_USE_SYSTEM_PREFERRED_RNG = 2; hAlgorithm = NULL (0). Returns STATUS_SUCCESS (0). }
function BCryptGenRandom(hAlgorithm: Cardinal; pbBuffer: AnsiString; cbBuffer: Cardinal; dwFlags: Cardinal): Integer;
  external 'BCryptGenRandom@bcrypt.dll stdcall';

{ Run a program hidden and wait; returns True on exit code 0. }
function RunWait(const FileName, Params, WorkingDir: string; var ResultCode: Integer): Boolean;
begin
  Result := Exec(FileName, Params, WorkingDir, SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Full path to the published API executable, which also hosts the Local console verbs used below
// (harden-permissions, protect-credentials, read-credentials) alongside provision-cert.
function ApiExecutable: string;
begin
  Result := ExpandConstant('{app}\api\ClinicManagement.API.exe');
end;

// Run an API console verb hidden, CAPTURING stdout+stderr so the verb's own French message can be shown to
// the operator instead of a bare exit code (Finding 5 -- surface the real reason, never just a code).
// Returns True on exit code 0; the captured output is left in LastVerbOutput either way.
function RunApiVerbQuiet(const Params: string): Boolean;
var
  Rc: Integer;
  LogFile, CmdLine: string;
begin
  Result := False;
  LastVerbOutput := '';
  LogFile := ExpandConstant('{tmp}\api-verb.log');

  // Wrapped in cmd.exe so the redirection applies. The nested quoting needs the outer "" cmd /C wrapper --
  // the same shape the initdb invocation below uses.
  CmdLine := '/C ""' + ApiExecutable + '" ' + Params + ' > "' + LogFile + '" 2>&1"';

  if not Exec(ExpandConstant('{sys}\cmd.exe'), CmdLine, ExpandConstant('{app}\api'),
              SW_HIDE, ewWaitUntilTerminated, Rc) then
  begin
    LastVerbOutput := 'Impossible de lancer ' + ApiExecutable;
    Exit;
  end;

  LoadStringFromFile(LogFile, LastVerbOutput);
  DeleteFile(LogFile);
  Result := (Rc = 0);
end;

// As RunApiVerbQuiet, but reports the failure to the operator. For steps that MUST abort the install: a
// permission or encryption step that cannot be applied has to fail loud, never silently leave patient data
// readable or passwords in cleartext (spec AC-1.4 / AC-2.9).
function RunApiVerb(const Params, StepDescription: string): Boolean;
begin
  Result := RunApiVerbQuiet(Params);
  if not Result then
    MsgBox('Échec de ' + StepDescription + '.' + #13#10#13#10 +
           'Détail :' + #13#10 + String(LastVerbOutput) + #13#10#13#10 +
           'Installation interrompue.', mbError, MB_OK);
end;

// Secure one or more already-quoted directory paths: inheritance broken, access reserved to the service
// account / LocalSystem / Administrators, and any grant to the local Users group or Everyone removed
// recursively. Delegates to the API so the policy has exactly one (unit-tested) implementation.
function HardenDirectories(const QuotedPaths, StepDescription: string): Boolean;
begin
  Result := RunApiVerb('harden-permissions ' + QuotedPaths, StepDescription);
end;

function Quoted(const Path: string): string;
begin
  Result := '"' + Path + '"';
end;

// The console verbs refuse to run outside Local mode, and Auth:Mode=Local is set by the generated
// appsettings.Production.json -- which WriteProductionConfig cannot produce until the DB password exists.
// So make sure a minimal Local-mode overlay is present BEFORE the first verb call. On a reinstall the full
// file already survives from the previous install and this is a no-op; WriteProductionConfig overwrites it
// with the complete configuration later either way.
procedure EnsureLocalModeConfig;
var
  CfgPath: string;
begin
  // L4e: seeded into the INSTALL layer, which WriteInstallConfig later overwrites in full. Seeding the
  // operator layer instead would create the very file EnsureOperatorConfig must not overwrite, and its
  // one-key content would then be frozen for the life of the install.
  CfgPath := ExpandConstant('{app}\api\appsettings.Install.json');
  if not FileExists(CfgPath) then
    SaveStringToFile(CfgPath, '{ "Auth": { "Mode": "Local" } }' + #13#10, False);
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

{ Per-install file that persists the generated DB passwords (clinic_user on line 1, postgres superuser on
  line 2) so a REINSTALL over an existing PostgreSQL cluster reuses them instead of regenerating and then
  failing authentication against the existing role. Colocated in {app}\api\.local (gitignored, never
  LAN-facing) — the SAME folder the API writes its other per-install secrets to (signing key, server.pfx,
  ca.crt, google-refresh-token) via AppContext.BaseDirectory, so one .local backup covers them all. }
function DbCredentialsFile: string;
begin
  Result := ExpandConstant('{app}\api\.local\db-credentials');
end;

{ Establish DbPassword + PgSuperPassword for this install. Returns False (with a clear operator message)
  when it cannot safely proceed, so the caller aborts instead of bootstrapping against mismatched creds.

  - Reinstall over an existing cluster: REUSE the persisted passwords (regenerating would break auth
    against the existing role → the "Échec de la création du rôle/de la base" abort + a forced data wipe).
  - Fresh install (no cluster, no usable credentials file): generate new random passwords and PERSIST them.
  - Existing cluster but the credentials file is missing/unreadable: FAIL LOUD and abort — silently
    generating new passwords would not match the existing cluster (recovery is documented in the README). }
function EstablishDbCredentials: Boolean;
var
  CredFile, PgData, PlainFile: string;
  Lines: TArrayOfString;
  ClusterExists, Recovered: Boolean;
begin
  Result := False;
  CredFile := DbCredentialsFile;
  PgData   := ExpandConstant('{app}\pgdata');
  ClusterExists := FileExists(PgData + '\PG_VERSION');

  if FileExists(CredFile) then
  begin
    // The persisted file is ENCRYPTED (machine-scoped) as of this release, so it can no longer be read
    // directly -- the read-credentials verb decrypts it to a temp file we delete immediately, mirroring the
    // existing pg-super.pw pattern. The verb also accepts a legacy PLAINTEXT file written by an earlier
    // installer and migrates it to the encrypted form in the same pass (spec AC-3.3), so an upgrade over an
    // existing install is remediated rather than left as-is.
    Recovered := False;
    PlainFile := ExpandConstant('{tmp}\db-credentials.plain');

    if RunApiVerbQuiet('read-credentials --out ' + Quoted(PlainFile)) then
    begin
      { The index access is nested under the length guard because Inno's Pascal Script does not guarantee
        short-circuit boolean evaluation. }
      if LoadStringsFromFile(PlainFile, Lines) and (GetArrayLength(Lines) >= 2) then
      begin
        if (Trim(Lines[0]) <> '') and (Trim(Lines[1]) <> '') then
        begin
          DbPassword      := Trim(Lines[0]);
          PgSuperPassword := Trim(Lines[1]);
          Recovered := True;
        end;
      end;
    end;

    { Delete the decrypted copy whether or not it parsed -- it must never outlive this function. }
    DeleteFile(PlainFile);

    if Recovered then
    begin
      Result := True;
      Exit;
    end;
    { File present but unreadable/corrupt: if a cluster also exists we cannot recover the real passwords. }
    if ClusterExists then
    begin
      MsgBox('Le fichier d''identifiants de la base (' + CredFile + ') est illisible ou incomplet, alors ' +
             'qu''un cluster PostgreSQL existe déjà. Impossible de récupérer les mots de passe existants. ' +
             'Installation interrompue.' + #13#10#13#10 +
             'Restaurez ce fichier depuis une sauvegarde, ou supprimez volontairement le dossier "pgdata" ' +
             'pour repartir de zéro (les données existantes seront perdues).', mbError, MB_OK);
      Exit;
    end;
    { No cluster → the corrupt file is harmless; fall through and regenerate. }
  end
  else if ClusterExists then
  begin
    { Cluster exists but no persisted credentials — cannot derive the existing passwords. Fail loud. }
    MsgBox('Un cluster PostgreSQL existe déjà (' + PgData + ') mais aucun fichier d''identifiants (' +
           CredFile + ') n''a été trouvé. Impossible de réutiliser les mots de passe existants. ' +
           'Installation interrompue.' + #13#10#13#10 +
           'Restaurez le fichier d''identifiants depuis une sauvegarde, ou supprimez volontairement le ' +
           'dossier "pgdata" pour repartir de zéro (les données existantes seront perdues).', mbError, MB_OK);
    Exit;
  end;

  { Fresh install (no cluster, no usable credentials file): generate new random passwords and persist. }
  DbPassword      := NewRandomPassword;
  PgSuperPassword := NewRandomPassword;
  ForceDirectories(ExpandConstant('{app}\api\.local'));

  // Secure the secrets directory BEFORE the plaintext passwords are written into it, so they are never
  // readable by another local account -- not even for the few milliseconds between write and encryption.
  if not HardenDirectories(Quoted(ExpandConstant('{app}\api\.local')),
                           'la sécurisation du dossier des secrets') then
    Exit;

  if not SaveStringToFile(CredFile, DbPassword + #13#10 + PgSuperPassword + #13#10, False) then
  begin
    MsgBox('Impossible d''écrire le fichier d''identifiants de la base (' + CredFile + '). ' +
           'Installation interrompue.', mbError, MB_OK);
    Exit;
  end;

  // Encrypt the credentials file at rest (machine-scoped, via the same Data Protection key ring the API
  // uses) so a stolen disk or a copy of the install folder yields no PostgreSQL passwords -- audit
  // section 2 finding 4. Fails loud: a credentials file left in cleartext is the defect, not a warning.
  if not RunApiVerb('protect-credentials', 'le chiffrement des identifiants de la base') then
    Exit;

  Result := True;
end;

{ ============================================================================================
  L4e -- config is written in TWO files, split by OWNERSHIP, and an upgrade no longer destroys the
  operator's own values.

  What it used to do: SaveStringToFile(appsettings.Production.json, Cfg, False) -- truncate --
  unconditionally from ssPostInstall, with no "if not FileExists" guard, ALTHOUGH the author used that
  exact idiom 25 lines away to gate initdb. So every upgrade silently erased every hand-edited value:
  Cors:AllowedOrigins, Hosting:TrustPort, Security:EnableHsts, the reminder gateway keys -- all of them
  documented in ..\README.md as things an operator edits by hand.

  What it does now:
    - appsettings.Install.json    installer-owned, machine-derived (connection string, bundled tool
                                  paths, ports). REWRITTEN every install, because those values are about
                                  THIS machine and a stale one is a broken install.
    - appsettings.Production.json operator-owned. Written once when absent, with every key the README
                                  tells operators to edit, and NEVER truncated again. The API loads it
                                  AFTER the install layer, so an operator's value always wins.

  A structural split rather than a JSON merge in Pascal, for one reason worth stating: a merge has to
  decide what to do about a key the operator DELIBERATELY REMOVED, and both answers are wrong. Two files
  make the question disappear. The API side is Startup\InstallConfiguration.cs.

  Any pre-existing Production.json is copied to .bak-<timestamp> before anything else happens, so even a
  bug in this procedure cannot be the end of an operator's configuration.
  ============================================================================================ }
procedure BackupExistingConfig(const CfgPath: string);
var
  Stamp: string;
begin
  if not FileExists(CfgPath) then
    Exit;

  Stamp := GetDateTimeString('yyyymmdd-hhnnss', '-', '-');
  // Best-effort: a failed copy must not abort the install, but it is logged by Inno's own log.
  FileCopy(CfgPath, CfgPath + '.bak-' + Stamp, False);
end;

{ Installer-owned layer: everything derived from this machine. Rewritten on every install. }
procedure WriteInstallConfig;
var
  Cfg, PgDump, PgRestore, Files, Backups, ConnStr, AppDir: string;
begin
  AppDir := ExpandConstant('{app}');
  PgDump    := AppDir + '\postgres\bin\pg_dump.exe';
  PgRestore := AppDir + '\postgres\bin\pg_restore.exe';
  Files     := AppDir + '\api\Files';
  Backups   := AppDir + '\api\Backups';
  ConnStr := 'Host=localhost;Port={#DbPort};Database={#DbName};Username={#DbUser};Password=' + DbPassword;

  { Escape backslashes for JSON. }
  StringChangeEx(PgDump, '\', '\\', True);
  StringChangeEx(PgRestore, '\', '\\', True);
  StringChangeEx(Files, '\', '\\', True);
  StringChangeEx(Backups, '\', '\\', True);
  StringChangeEx(ConnStr, '\', '\\', True);

  Cfg :=
    '{' + #13#10 +
    '  "Auth": { "Mode": "Local" },' + #13#10 +
    '  "ConnectionStrings": { "DefaultConnection": "' + ConnStr + '" },' + #13#10 +
    '  "FileStorage": { "BasePath": "' + Files + '" },' + #13#10 +
    // L4b/L4c: a REAL default destination (not ""), and pg_restore beside pg_dump so a backup can be
    // verified readable -- an unverified dump is not a backup, and the tool ships in the same folder.
    '  "Backup": {' + #13#10 +
    '    "PgDumpPath": "' + PgDump + '",' + #13#10 +
    '    "PgRestorePath": "' + PgRestore + '",' + #13#10 +
    '    "DefaultDestination": "' + Backups + '",' + #13#10 +
    '    "TimeoutSeconds": 1800' + #13#10 +
    '  },' + #13#10 +
    // TrustPort is written explicitly rather than left to the API's own default: the firewall rule
    // opens {#TrustPort}, and a config that fell back to a different default would open a port nothing
    // listens on while the page advertised a port the firewall blocks. One number, stated once.
    '  "Hosting": { "HttpPort": {#HttpPort}, "HttpsPort": {#HttpsPort}, "WebPort": {#WebPort}, "TrustPort": {#TrustPort} },' + #13#10 +
    '  "Https": { "CertPath": "" }' + #13#10 +
    '}' + #13#10;

  SaveStringToFile(AppDir + '\api\appsettings.Install.json', Cfg, False);
end;

{ Operator-owned layer: written ONCE, when absent. Carries every key ..\README.md tells an operator to
  hand-edit, with its default value, so the file is a menu rather than a blank page -- a generator that
  writes fewer keys than the file legitimately holds is the bug (the spec's own wording). }
procedure EnsureOperatorConfig;
var
  Cfg, CfgPath: string;
begin
  CfgPath := ExpandConstant('{app}\api\appsettings.Production.json');

  { The pre-L4e installs wrote the FULL machine config here. Leave such a file completely alone: its
    values are correct, they now simply sit above an install layer that repeats some of them, and the
    operator layer wins -- which is the intended outcome either way. }
  if FileExists(CfgPath) then
    Exit;

  Cfg :=
    '{' + #13#10 +
    '  // Ce fichier est le VOTRE : l''installateur ne le remplace jamais.' + #13#10 +
    '  // Les valeurs propres a cette machine (base de donnees, chemins, ports) sont dans' + #13#10 +
    '  // appsettings.Install.json, qui est regenere a chaque installation. Ce que vous ecrivez ici' + #13#10 +
    '  // a la priorite. Voir README.md.' + #13#10 +
    '' + #13#10 +
    '  // HSTS : LAISSE A false EN LOCAL. Une fois memorise par un navigateur, il n''y a plus de' + #13#10 +
    '  // "continuer quand meme" possible sur un certificat auto-signe.' + #13#10 +
    '  "Security": { "EnableHsts": false },' + #13#10 +
    '' + #13#10 +
    '  // Origines supplementaires autorisees (postes du reseau local), ex. "https://192.168.1.20:5001".' + #13#10 +
    '  "Cors": { "AllowedOrigins": [] },' + #13#10 +
    '' + #13#10 +
    '  // Mettre TrustPort a 0 desactive entierement la page d''installation du certificat.' + #13#10 +
    '  "Hosting": { "TrustPort": {#TrustPort} },' + #13#10 +
    '' + #13#10 +
    '  // Version minimale des applications mobiles (Android / iOS) acceptee par ce serveur.' + #13#10 +
    '  // VIDE = aucune limite : toutes les versions sont acceptees, y compris le navigateur.' + #13#10 +
    '  // Renseignez MinimumShellVersion (ex. "1.2.0") pour refuser les applications trop anciennes :' + #13#10 +
    '  // elles afficheront alors un ecran "Mise a jour requise" avec le lien du magasin ci-dessous.' + #13#10 +
    '  // Prise en compte immediate, sans redemarrer le service.' + #13#10 +
    '  "Clients": {' + #13#10 +
    '    "MinimumShellVersion": "",' + #13#10 +
    '    "CurrentShellVersion": "",' + #13#10 +
    '    "StoreUrls": { "Android": "", "Ios": "" }' + #13#10 +
    '  },' + #13#10 +
    '' + #13#10 +
    '  // Rappels SMS / WhatsApp : les identifiants se saisissent dans l''application' + #13#10 +
    '  // (Rappels -> Configurer les canaux). Ces cles ne servent que de valeurs par defaut' + #13#10 +
    '  // pour toute l''installation.' + #13#10 +
    '  "Reminders": {' + #13#10 +
    '    "Channels": [],' + #13#10 +
    '    "LeadTimesHours": [ 24, 6 ],' + #13#10 +
    '    "QuietHoursStartLocal": 21,' + #13#10 +
    '    "QuietHoursEndLocal": 8' + #13#10 +
    '  }' + #13#10 +
    '}' + #13#10;

  SaveStringToFile(CfgPath, Cfg, False);
end;

{ Kept as the single entry point the install step calls, so the ordering lives in one place. }
procedure WriteProductionConfig;
var
  CfgPath: string;
begin
  CfgPath := ExpandConstant('{app}\api\appsettings.Production.json');
  BackupExistingConfig(CfgPath);
  WriteInstallConfig;
  EnsureOperatorConfig;
end;

{ initdb a fresh cluster (password-enforced), register + start the PostgreSQL service, then create the DB +
  role. Returns False on ANY hard failure so the caller aborts instead of proceeding against a missing
  role/DB and reporting "success" (Finding 5). Auth is scram-sha-256, not trust, so no local OS account can
  connect password-less (Finding 10); psql authenticates via a temporary pgpass.conf that is deleted after
  bootstrap. }
function SetupPostgres: Boolean;
var
  Rc: Integer;
  I: Integer;
  DbReady: Boolean;
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

      // AC-1.7: revoke the Users grant on the FAILURE path too. An aborted install must not leave the
      // cluster directory world-readable while the operator believes the install simply failed cleanly.
      // Best-effort here (the install is already aborting; a message about permissions on top of the real
      // initdb error would only obscure it), but it must be attempted.
      HardenDirectories(Quoted(PgData), 'la sécurisation du dossier de la base');

      MsgBox('Échec de l''initialisation de PostgreSQL (initdb, code ' + IntToStr(Rc) + ').' + #13#10#13#10 +
             'Détail :' + #13#10 + LogText + #13#10#13#10 +
             'Journal complet : ' + InitLog + #13#10 + 'Installation interrompue.', mbError, MB_OK);
      Exit;
    end;

    // AC-1.2: initdb has finished, so the Full Control that BUILTIN\Users needed in order to run it is
    // revoked IMMEDIATELY -- the grant is scoped to the one step that genuinely requires it. This is the
    // headline P0: it was previously never taken away, leaving every local account read/write over the
    // whole cluster holding all patient records. Because that grant was inheritable, removing it here also
    // clears it from everything initdb created (AC-1.3).
    if not HardenDirectories(Quoted(PgData), 'la sécurisation du dossier de la base') then
      Exit;
  end;

  { Register as an auto-start service (bind loopback only — the DB is never LAN-facing). Tolerate
    "already registered"/"already running" on re-install; the readiness probe below is the real gate. }
  RunWait(PgCtl, 'register -N "{#ServiceDb}" -D "' + PgData + '" -S auto -o "-p {#DbPort} -h 127.0.0.1"', PgBin, Rc);
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceDb}', '', SW_HIDE, ewWaitUntilTerminated, Rc);

  { Wait for readiness — POLL pg_isready in a loop. pg_isready does ONE connection attempt and returns
    "no response" immediately when the port isn't open yet (the -t timeout only applies to a hanging
    connect, not a refused one), so a single call races the service's startup — which can take many
    seconds while Defender/SAC scans the freshly-extracted postgres.exe on first run. Retry ~60s; hard-fail
    only if the server never comes up. }
  DbReady := False;
  for I := 1 to 60 do
  begin
    if RunWait(PgBin + '\pg_isready.exe', '-h 127.0.0.1 -p {#DbPort} -t 2', PgBin, Rc) then
    begin
      DbReady := True;
      Break;
    end;
    Sleep(1000);
  end;
  if not DbReady then
  begin
    MsgBox('PostgreSQL ne répond pas (pg_isready) après 60 s. Installation interrompue.', mbError, MB_OK);
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

{ Open the HTTPS front door and the device-trust page on the LAN firewall -- and nothing else. }
procedure OpenFirewall;
var
  Rc: Integer;
begin
  Exec(ExpandConstant('{sys}\netsh.exe'),
    'advfirewall firewall add rule name="Clinic Management HTTPS" dir=in action=allow protocol=TCP localport={#HttpsPort}',
    '', SW_HIDE, ewWaitUntilTerminated, Rc);

  // The device-trust page (P8). Cleartext on purpose and safe on purpose: a phone cannot be asked to fetch
  // the certificate fix over the certificate it does not trust yet, so this one page has to be reachable
  // without TLS. The API refuses every other path on this port (TrustPortGate), so what is exposed here is a
  // CA's PUBLIC certificate, install instructions and a QR -- not the API. Removed again on uninstall.
  Exec(ExpandConstant('{sys}\netsh.exe'),
    'advfirewall firewall add rule name="Clinic Management Trust" dir=in action=allow protocol=TCP localport={#TrustPort}',
    '', SW_HIDE, ewWaitUntilTerminated, Rc);
end;

{ Provision the HTTPS cert at INSTALL time, start web then API, then export the CA for clients. }
procedure StartAndExportCa;
var
  Rc, Tries: Integer;
  ApiExe, CaSrc, CaDst: string;
begin
  { Generate (or reuse) the CA + server cert NOW, before the API service starts. On a fresh install the
    service's first boot would otherwise generate the cert on top of first-run JIT and can miss the ~30s
    Windows SCM start window; provisioning here moves that work off the SCM clock. Idempotent (reuses an
    existing set) and makes no DB connection. If it fails the service still self-generates on boot — at
    the risk of the SCM timeout — so warn but do not abort. }
  ApiExe := ExpandConstant('{app}\api\ClinicManagement.API.exe');
  { Init to a non-zero sentinel: RunWait returns False without setting Rc if the exe fails to launch, and
    Inno zero-inits locals — so an unset Rc would report a misleading "code 0" (reads as success). }
  Rc := -1;
  if not RunWait(ApiExe, 'provision-cert', ExpandConstant('{app}\api'), Rc) then
    MsgBox('Avertissement : la génération du certificat HTTPS à l''installation a échoué (code ' +
           IntToStr(Rc) + '). Le service API tentera de le générer à son premier démarrage.',
           mbInformation, MB_OK);

  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceWeb}', '', SW_HIDE, ewWaitUntilTerminated, Rc);
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceApi}', '', SW_HIDE, ewWaitUntilTerminated, Rc);

  // The API writes .local/ next to its own exe (AppContext.BaseDirectory = {app}\api), so the CA is at
  // {app}\api\.local\ca.crt -- NOT {app}\.local\ca.crt (which left %ProgramData%\...\ca.crt empty).
  // NOTE: this must stay a // comment. Inno's Pascal { } comments do not nest, so the first } -- the one
  // closing {app} -- would terminate the comment early and leave the rest of the line as code.
  CaSrc := ExpandConstant('{app}\api\.local\ca.crt');
  CaDst := ExpandConstant('{commonappdata}\ClinicManagement\ca.crt');

  { Provisioned above at install time; poll briefly as a fallback in case the service generated it. }
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
           '{app}\api\.local\ca.crt vers un support partagé pour l''installateur client (voir README).',
           mbInformation, MB_OK);
end;

// Secure every directory holding patient data or per-install secrets, and remove the initdb transcript.
// Runs BEFORE the services are registered/started so the API service never observes a permissive state.
// Idempotent, so it also remediates an install created by an earlier installer version (spec AC-1.6 /
// AC-2.7 -- the upgrade path, which is how most existing clinics will receive this fix).
function HardenInstallDirectories: Boolean;
var
  Paths, InitLog: string;
begin
  Paths := Quoted(ExpandConstant('{app}\api\.local')) + ' ' +
           Quoted(ExpandConstant('{app}\api\Files')) + ' ' +
           Quoted(ExpandConstant('{app}\api\logs')) + ' ' +
           // L4b: a backup folder is a full copy of every patient record, so it gets the same posture as
           // the live data. PgDumpBackupService hardens each timestamped subfolder as well; this secures
           // the root so a folder is never briefly readable between creation and hardening.
           Quoted(ExpandConstant('{app}\api\Backups')) + ' ' +
           Quoted(ExpandConstant('{app}\pgdata'));

  Result := HardenDirectories(Paths, 'la sécurisation des droits d''accès aux données');
  if not Result then
    Exit;

  // AC-2.8: initdb's transcript is written into the install root, which stays readable by every local
  // account (only the four directories above are secured). It has served its purpose once the install
  // succeeds, and it can echo cluster detail, so remove it rather than leave it exposed.
  InitLog := ExpandConstant('{app}\initdb.log');
  if FileExists(InitLog) then
    DeleteFile(InitLog);
end;

{ ============================================================================================
  L4f -- stop the running services BEFORE any file is copied over them.

  [Files] copied the whole api\, web\ and node\ trees while the API and Node services were STILL
  RUNNING: the only teardown in the script lived inside SetupAppServices, which runs from ssPostInstall,
  i.e. AFTER the copy. Neither .iss had a PrepareToInstall, a CloseApplications or a ServicesStopped of
  any kind. On Windows a running executable's image is locked, so the outcome was one of two bad ones --
  the copy fails and the upgrade silently ships a half-updated tree, or it succeeds partially and the
  service restarts on a mix of old and new assemblies.

  PrepareToInstall is the correct hook: Inno calls it after the wizard and BEFORE the file copy, and a
  non-empty return value aborts the install with that message. It is deliberately tolerant of "service
  not found" (a first install has none) -- sc.exe's exit code is ignored for exactly that reason.
  ============================================================================================ }
procedure StopClinicServices;
var
  Rc: Integer;
  Nssm: string;
begin
  Nssm := ExpandConstant('{app}\tools\nssm.exe');

  { API first, then the web front end it proxies to: stopping the proxy target first would leave the
    front door briefly answering 502s to anyone still on a page. }
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceApi}', '', SW_HIDE, ewWaitUntilTerminated, Rc);

  if FileExists(Nssm) then
    RunWait(Nssm, 'stop {#ServiceWeb}', '', Rc)
  else
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceWeb}', '', SW_HIDE, ewWaitUntilTerminated, Rc);

  { `sc stop` returns as soon as the STOP control is ACCEPTED, not once the process has exited -- so
    without this wait the copy can still hit a locked image. Kestrel and Node both shut down in well
    under this; the cost is a few seconds on an upgrade nobody is watching. }
  Sleep(5000);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopClinicServices;
  { '' = proceed. Nothing here is fatal: a first install has no services to stop, and a service that
    refuses to stop surfaces as a file-in-use error from the copy itself, which Inno already reports
    with a retry. }
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // The console verbs invoked from here refuse to run outside Local mode, and Auth:Mode=Local lives in the
    // generated appsettings.Production.json -- which WriteProductionConfig cannot write until the DB
    // password exists. Seed a minimal Local overlay first so the ordering works on a fresh install.
    EnsureLocalModeConfig;

    { Establish DB credentials FIRST — generate + persist on a fresh install, reuse on reinstall over an
      existing cluster. Abort (skip the rest) if it cannot proceed safely. Runs before WriteProductionConfig
      because the connection string bakes in DbPassword. The .local/ dir was already created by [Dirs]. }
    if not EstablishDbCredentials then
      Exit;

    WriteProductionConfig;
    if SetupPostgres then
    begin
      // Secure the data directories before anything starts consuming them. Aborts the install on failure --
      // completing with patient records readable by every local account is not an acceptable outcome.
      if not HardenInstallDirectories then
        Exit;

      SetupAppServices;
      OpenFirewall;
      StartAndExportCa;
    end;
  end;
end;
