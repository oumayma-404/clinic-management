; ============================================================================================
; APEXA — Installer (Local / offline-LAN)
;
; ONE installer, and the first page asks what this PC is.
;
;   « Serveur du cabinet » — the whole stack, unchanged from the installer this replaces:
;       - bundled PostgreSQL 16 (fresh cluster, auto-start service, clinic_management + clinic_user)
;       - the .NET API as an auto-start Windows service (Kestrel = sole LAN-facing HTTPS front door)
;       - the Next.js web bundle as a localhost-only Node service (reverse-proxied by Kestrel)
;       - Local config written on the target; signing key + HTTPS cert self-generated on first boot
;     Dependency order: PostgreSQL -> Web (Node) -> API front door. Only the HTTPS port is opened on
;     the LAN firewall; the Node web port and the API's plain-HTTP port stay loopback-only.
;
;   « Poste de travail » — carries NO payload at all. It asks for the server's address, fetches that
;     server's certificate authority over the cleartext trust port, shows its fingerprint for a human
;     to accept, imports it, then downloads and runs the shell's own setup from the same server.
;
; ⚠️ WHY THIS REPLACED TWO INSTALLERS, because « one file is tidier » was not the reason.
;
;   (1) NO CLIENT INSTALLER EVER SHIPPED WITH A CERTIFICATE AUTHORITY. `clinic-client.iss` took its
;       `ca.crt` from `build-output\client\ca\`, staged BY HAND, with `skipifsourcedoesntexist` — and a
;       CA is minted on the clinic's own server at ITS first boot, which on a build machine has not
;       happened and never will. So every compiled client setup imported nothing, silently by design
;       (« absent is a valid state »), and every staff PC met a certificate warning. Fetching the CA
;       from the server the user just named is the only version of this that can be correct, and it
;       removes the per-clinic rebuild that was never actually being done.
;
;   (2) A SHELL INSTALLED BY INNO COULD NEVER UPDATE ITSELF. `ShellUpdater.CheckAndStageAsync` returns
;       null when `UpdateManager.IsInstalled` is false, which is exactly what an Inno install under
;       %ProgramFiles% is — so « Mettre à jour maintenant » did nothing, for ever, with no error. The
;       poste branch runs the VELOPACK setup instead (per-user, delta, no UAC), which is the only
;       channel where the update path works. `/api/meta/client-download` already prefers the Velopack
;       package over the legacy Inno one when both are present, so nothing in the API changes.
;
; R-1: committed-but-not-executed here — build the payload with ..\publish-server.ps1 on an operator
; build machine. It does now COMPILE in CI though: `.github/workflows/ci.yml` § installers runs ISCC
; on windows-latest against stub payloads, which is what this file went months without.
; ============================================================================================

#define AppName        "APEXA"
#ifndef AppVersion
  ; The fallback for a hand-run `ISCC.exe this.iss`. `publish-server.ps1 -Version x.y.z` passes
  ; /DAppVersion and WINS over this — see ..\README.md § « Publier une mise a jour du shell ».
  ; It must stay in step with the shell assembly's own <Version>, because that assembly version is what
  ; the running shell reports as X-Client-Version and what the server's floor is compared against: ship an
  ; installer named 1.1.0 around a binary reporting 1.0.0 and your own floor refuses the build you just
  ; shipped, with nothing anywhere naming the mismatch.
  #define AppVersion   "1.0.0"
#endif
#define AppPublisher   "APEXA"
; The product mark, shared with the client installer and the shell .exe. Generated from the one master
; (web/branding/icon.svg) by web/scripts/generate-icons.mjs.
#define AppIcon        SourcePath + "\..\..\desktop\ClinicManagement.DesktopShell\Assets\app.ico"
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
; ⚠️ NOT renamed with the product, and here it is load-bearing rather than merely tidy: the API resolves its
; config, file storage, logs and the PostgreSQL cluster relative to this directory (R-6), three Windows services
; point at absolute paths under it, and the operator guide names it. Inno reuses the recorded directory on
; upgrade, so this string only names a fresh install -- but moving it would still split new clinics from every
; deployed one for a folder nobody looks at.
DefaultDirName={autopf}\Clinic Management
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#SourcePath}\..\build-output
; ASCII and stable: this filename is typed into a URL and printed in the operator guide.
OutputBaseFilename=ClinicManagementSetup-{#AppVersion}
SetupIconFile={#AppIcon}
UninstallDisplayName={#AppName}
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
; Protection key ring and every uploaded radiograph were readable by every local
; account). The directories are created here and SECURED after install by the API's `harden-permissions`
; console verb, which breaks inheritance and removes Users/Everyone -- one testable implementation, shared
; with the one-click backup so the two cannot drift.
; ⚠️ Every entry carries `Check: IsServerRole`. A « poste de travail » install creates NONE of these: it holds
; no database, no blobs and no config, and an empty api\Backups on a secretary's PC would be a destination the
; backup screen could offer on a machine with nothing to back up.
Name: "{app}\api\.local"; Check: IsServerRole
Name: "{app}\api\Files"; Check: IsServerRole
Name: "{app}\api\logs"; Check: IsServerRole
; L4b -- the real default backup destination. The config used to carry "" for it while the settings
; screen said "leave the field blank to use the server default folder", so the documented default path
; failed on every fresh install. Created here and hardened with the other data directories below.
Name: "{app}\api\Backups"; Check: IsServerRole
; Where the matching client installer is staged, so a clinic's own server can serve the shell update to its
; own PCs (ClientUpdatePackage -> GET /api/meta/client-download). On an offline LAN this is what makes
; « Mettre a jour maintenant » able to FETCH an update rather than only announce one.
Name: "{app}\updates"; Check: IsServerRole
Name: "{app}\pgdata"; Check: IsServerRole
Name: "{commonappdata}\ClinicManagement"; Check: IsServerRole

[Files]
; Payloads staged by ..\publish-server.ps1 into build-output\server\.
Source: "{#SourcePath}\..\build-output\server\api\*";      DestDir: "{app}\api";      Flags: recursesubdirs createallsubdirs ignoreversion; Check: IsServerRole
Source: "{#SourcePath}\..\build-output\server\web\*";      DestDir: "{app}\web";      Flags: recursesubdirs createallsubdirs ignoreversion; Check: IsServerRole
Source: "{#SourcePath}\..\build-output\server\node\*";     DestDir: "{app}\node";     Flags: recursesubdirs createallsubdirs ignoreversion; Check: IsServerRole
Source: "{#SourcePath}\..\build-output\server\postgres\*"; DestDir: "{app}\postgres"; Flags: recursesubdirs createallsubdirs ignoreversion; Check: IsServerRole
; NSSM (Non-Sucking Service Manager) hosts the Node web server as a Windows service (R-8).
; Operator drops nssm.exe into packaging\server\tools\ before compiling; optional at compile time.
Source: "{#SourcePath}\..\server\tools\nssm.exe"; DestDir: "{app}\tools"; Flags: ignoreversion skipifsourcedoesntexist; Check: IsServerRole
; The client installer this release was built with, staged by ..\publish-server.ps1 (which compiles the
; client FIRST for exactly this reason). Served by the API to shells asking for an update.
; ⚠️ skipifsourcedoesntexist: a -SkipInstallers staging run has no client setup to copy, and a server
; installer without an update payload is correct-but-reduced (clients then need Clients:StoreUrls:Windows),
; not broken. The API answers 404 on the download route when the folder is empty.
Source: "{#SourcePath}\..\build-output\server\updates\*"; DestDir: "{app}\updates"; Flags: ignoreversion skipifsourcedoesntexist; Check: IsServerRole

[Icons]
Name: "{group}\APEXA (serveur — localhost)"; Filename: "https://localhost:{#HttpsPort}"; Check: IsServerRole
Name: "{group}\Désinstaller {#AppName}"; Filename: "{uninstallexe}"

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
; The clinic's certificate authority, imported by whichever role installed it. Matched on the CA's SUBJECT,
; which CertificateProvisioner freezes at this exact string -- renaming it there orphans every installed CA.
; ⚠️ No Check: the entries above have none either, and deliberately: all of them are best-effort removals of
; something that may not exist, and `runhidden` swallows the "not found" exit. Making them role-conditional
; would mean persisting the role somewhere the UNINSTALLER can read it, which is a second source of truth
; about a machine whose role can only have been one thing.
Filename: "{sys}\certutil.exe"; Parameters: "-delstore Root ""Clinic Management Local CA"""; Flags: runhidden; RunOnceId: "DelCa"

[Code]
{ SW_HIDE is a built-in Inno Setup constant — do not redeclare it (duplicate-identifier compile error). }

var
  DbPassword: string;       { clinic_user login password (also baked into the connection string) }
  PgSuperPassword: string;   { postgres superuser password (scram-sha-256, Finding 10) }
  LastVerbOutput: AnsiString; { stdout+stderr of the most recent API console verb, for operator messages }
  RolePage: TInputOptionWizardPage;  { page 1 -- « ce PC est le serveur » / « ce PC est un poste » }
  AddressPage: TInputQueryWizardPage; { page 2, poste only -- the server's name or address }
  BackupPage: TInputDirWizardPage;    { page 2, server only -- where the nightly backup is written }

// Which of the two the operator picked. Consulted by every [Dirs], [Files] and [Icons] entry, so it must
// answer before any of them is evaluated -- which it does: Inno evaluates Check functions during the file
// copy, long after the wizard pages have been through.
//
// ⚠️ It answers TRUE before the wizard has run, and that is deliberate rather than an accident of ordering.
// Inno evaluates Check functions in contexts that never see the page (/SILENT with no /ROLE, a restarted
// setup resuming), and « server » is the answer that installs the product rather than the answer that
// installs nothing. An unattended install that silently produced an empty directory would be the worse
// failure, because it looks like it worked.
function IsServerRole: Boolean;
begin
  if RolePage = nil then
    Result := True
  else
    Result := (RolePage.SelectedValueIndex = 0);
end;

function IsWorkstationRole: Boolean;
begin
  Result := not IsServerRole;
end;

// What the operator typed on the address page, trimmed. Empty on a server install.
function TypedServerAddress: string;
begin
  if AddressPage = nil then
    Result := ''
  else
    Result := Trim(AddressPage.Values[0]);
end;

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

// ⚠️ `//` and not a `{ }` block: this comment names `{app}`, and inside a brace comment that constant's
//    own `}` CLOSES the comment early — the rest of the sentence then parses as code and ISCC reports
//    « Syntax error » on a line of prose. Same trap as the `[Files]` block further down, and between them
//    they broke this installer's compile outright — the last setup that built is dated months before the
//    comments that broke it, so the two landed with the payloads staged and ISCC never re-run.
//
// Per-install file that persists the generated DB passwords (clinic_user on line 1, postgres superuser on
// line 2) so a REINSTALL over an existing PostgreSQL cluster reuses them instead of regenerating and then
// failing authentication against the existing role. Colocated in {app}\api\.local (gitignored, never
// LAN-facing) — the SAME folder the API writes its other per-install secrets to (signing key, server.pfx,
// ca.crt, google-refresh-token) via AppContext.BaseDirectory, so one .local backup covers them all.
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

// ============================================================================================
// L4e -- config is written in TWO files, split by OWNERSHIP, and an upgrade no longer destroys the
// operator's own values.
//
// What it used to do: SaveStringToFile(appsettings.Production.json, Cfg, False) -- truncate --
// unconditionally from ssPostInstall, with no "if not FileExists" guard, ALTHOUGH the author used that
// exact idiom 25 lines away to gate initdb. So every upgrade silently erased every hand-edited value:
// Cors:AllowedOrigins, Hosting:TrustPort, Security:EnableHsts, the reminder gateway keys -- all of them
// documented in ..\README.md as things an operator edits by hand.
//
// What it does now:
// - appsettings.Install.json    installer-owned, machine-derived (connection string, bundled tool
// paths, ports). REWRITTEN every install, because those values are about
// THIS machine and a stale one is a broken install.
// - appsettings.Production.json operator-owned. Written once when absent, with every key the README
// tells operators to edit, and NEVER truncated again. The API loads it
// AFTER the install layer, so an operator's value always wins.
//
// A structural split rather than a JSON merge in Pascal, for one reason worth stating: a merge has to
// decide what to do about a key the operator DELIBERATELY REMOVED, and both answers are wrong. Two files
// make the question disappear. The API side is Startup\InstallConfiguration.cs.
//
// Any pre-existing Production.json is copied to .bak-<timestamp> before anything else happens, so even a
// bug in this procedure cannot be the end of an operator's configuration.
// ============================================================================================
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
  // The destination the operator chose on the backup page, falling back to the in-install folder when they
  // left it empty. ⚠️ That fallback is the ORIGINAL behaviour and it is the weak one: a dump beside the data
  // it dumps dies with the disk. It stays available because a single-PC cabinet with no external disk has
  // nowhere else, and the page says so in words before the operator accepts it -- what is not acceptable is
  // arriving there silently, which is what happened before this page existed.
  if (BackupPage <> nil) and (Trim(BackupPage.Values[0]) <> '') then
    Backups := Trim(BackupPage.Values[0])
  else
    Backups := AppDir + '\api\Backups';
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
    'create {#ServiceApi} binPath= "' + ApiExe + '" start= auto DisplayName= "APEXA - API" depend= ' + ApiDepend,
    '', SW_HIDE, ewWaitUntilTerminated, Rc);
  Exec(ExpandConstant('{sys}\sc.exe'), 'failure {#ServiceApi} reset= 60 actions= restart/5000', '', SW_HIDE, ewWaitUntilTerminated, Rc);
end;

{ Open the HTTPS front door and the device-trust page on the LAN firewall -- and nothing else.

  WARNING: the two rule names below are FROZEN at the English product name and must not be renamed with the
  product. They are identity keys, not labels: [UninstallRun] deletes each rule by name, so a rename here orphans
  the rule on every machine that installed a previous build -- an open LAN port with nothing left to remove it.
  The client installer's CA subject CN ("Clinic Management Local CA") is frozen for the same reason: it must match
  what the API's CertificateProvisioner already wrote into .local/ on every deployed server. }
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

// ============================================================================================
// L4f -- stop the running services BEFORE any file is copied over them.
//
// [Files] copied the whole api\, web\ and node\ trees while the API and Node services were STILL
// RUNNING: the only teardown in the script lived inside SetupAppServices, which runs from ssPostInstall,
// i.e. AFTER the copy. Neither .iss had a PrepareToInstall, a CloseApplications or a ServicesStopped of
// any kind. On Windows a running executable's image is locked, so the outcome was one of two bad ones --
// the copy fails and the upgrade silently ships a half-updated tree, or it succeeds partially and the
// service restarts on a mix of old and new assemblies.
//
// PrepareToInstall is the correct hook: Inno calls it after the wizard and BEFORE the file copy, and a
// non-empty return value aborts the install with that message. It is deliberately tolerant of "service
// not found" (a first install has none) -- sc.exe's exit code is ignored for exactly that reason.
// ============================================================================================
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

// ============================================================================================
// The role wizard, and the two post-install branches it decides between.
// ============================================================================================

procedure InitializeWizard;
begin
  // Page 1. Exclusive (radio) rather than a checkbox list: a PC is one thing or the other, and a wizard
  // that lets somebody tick both would have to invent an answer for it.
  RolePage := CreateInputOptionPage(
    wpWelcome,
    'Ce PC',
    'Quel rôle ce poste joue-t-il dans le cabinet ?',
    'APEXA s''installe une seule fois sur le PC qui garde les dossiers. Les autres postes s''y connectent.',
    True, False);
  RolePage.Add('Le serveur du cabinet — il garde les dossiers et les sauvegardes');
  RolePage.Add('Un poste de travail — il se connecte au serveur du cabinet');
  RolePage.SelectedValueIndex := 0;

  // Page 2a, poste only.
  AddressPage := CreateInputQueryPage(
    RolePage.ID,
    'Serveur du cabinet',
    'À quelle adresse ce poste doit-il se connecter ?',
    'Saisissez le NOM du PC serveur (par exemple clinic-server). Le nom est préférable à une adresse IP :'
    + ' une adresse change toute seule le jour où la box en attribue une autre, et ce poste ne se connecte'
    + ' alors plus. Le programme d''installation du serveur affiche le nom à utiliser.');
  AddressPage.Add('Nom ou adresse du serveur :', False);

  // Page 2b, server only. `TInputDirWizardPage` gives the folder browser for free, which matters because
  // the right answer is on a disk the operator has to go and find.
  BackupPage := CreateInputDirPage(
    RolePage.ID,
    'Sauvegarde',
    'Où les sauvegardes du cabinet doivent-elles être écrites ?',
    'Choisissez un disque externe ou un dossier réseau. Une sauvegarde posée sur le disque qui contient'
    + ' déjà les dossiers disparaît avec lui — ce n''est pas une sauvegarde, c''est une copie.',
    False, '');
  BackupPage.Add('');
end;

// The two role pages are mutually exclusive, and each is skipped for the other role.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  if PageID = AddressPage.ID then
    Result := IsServerRole
  else if PageID = BackupPage.ID then
    Result := IsWorkstationRole
  else
    Result := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Address: string;
begin
  Result := True;

  if CurPageID = AddressPage.ID then
  begin
    Address := TypedServerAddress;
    if Address = '' then
    begin
      MsgBox('Saisissez le nom ou l''adresse du serveur du cabinet.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    // Deliberately NOT validated further here, and not probed either. A name this installer cannot resolve
    // right now may resolve perfectly once the PC is on the clinic's own network, and refusing it would send
    // somebody away from an install that would have worked. The CA fetch below is where a wrong address is
    // actually found out, and it says so in words.
  end;

  if CurPageID = BackupPage.ID then
  begin
    // ⚠️ A WARNING, NOT A REFUSAL. A single-PC cabinet with no external disk and no share has nowhere else to
    // put it, and refusing to install the product over it would be absurd. What is not acceptable is the
    // previous behaviour: silently defaulting to a folder inside the install directory and never mentioning
    // that the backup shares the fate of what it is backing up.
    if (BackupPage.Values[0] = '')
       or (Uppercase(Copy(BackupPage.Values[0], 1, 3)) = Uppercase(Copy(ExpandConstant('{sd}'), 1, 3))) then
    begin
      if MsgBox('Les sauvegardes seront écrites sur le même disque que les dossiers du cabinet.' + #13#10#13#10 +
                'Si ce disque tombe en panne, les dossiers ET les sauvegardes sont perdus en même temps.' + #13#10#13#10 +
                'Continuer quand même ?', mbConfirmation, MB_YESNO) = IDNO then
        Result := False;
    end;
  end;
end;

// ---------------------------------------------------------------------------------------------
// Server role
// ---------------------------------------------------------------------------------------------

// Stop Windows putting the clinic's server to sleep.
//
// ⚠️ This is the single cheapest thing in this file and it prevents the loudest outage: a PC that suspends
// takes every desk in the practice down at once, the symptom is « le logiciel ne marche plus » on machines
// that are themselves fine, and nothing anywhere names the cause. A server is a server whether or not it also
// has somebody's keyboard attached to it.
//
// Only the AC (mains) timeouts are touched. The DC ones are left alone on purpose: a laptop acting as the
// clinic server and running on battery is going to stop being a server shortly regardless, and forbidding it
// to sleep would flatten it instead.
procedure DisableSleepOnMains;
var
  Rc: Integer;
begin
  // Best-effort: powercfg can be refused by group policy on a managed machine, and a clinic PC that will not
  // take the setting is a smaller problem than an install that aborts over it.
  RunWait(ExpandConstant('{sys}\powercfg.exe'), '/change standby-timeout-ac 0', '', Rc);
  RunWait(ExpandConstant('{sys}\powercfg.exe'), '/change hibernate-timeout-ac 0', '', Rc);
  RunWait(ExpandConstant('{sys}\powercfg.exe'), '/change disk-timeout-ac 0', '', Rc);
end;

// What the other PCs must be told to type. The whole point of preferring a name over an address is undone if
// the person installing the server never learns the name, so this is shown rather than logged.
procedure AnnounceServerAddress;
begin
  MsgBox('Le serveur du cabinet est installé.' + #13#10#13#10 +
         'Sur les AUTRES postes, lancez le même programme d''installation, choisissez'
         + ' « Un poste de travail », et saisissez :' + #13#10#13#10 +
         '        ' + GetComputerNameString + #13#10#13#10 +
         'C''est le nom de ce PC. Préférez-le à une adresse IP : il ne change pas.' + #13#10#13#10 +
         'Sur CE PC, l''application s''ouvre sur https://localhost:{#HttpsPort}',
         mbInformation, MB_OK);
end;

// ---------------------------------------------------------------------------------------------
// Workstation role
// ---------------------------------------------------------------------------------------------

// Run a command line through cmd.exe capturing stdout+stderr, as RunApiVerbQuiet does for the API verbs.
// Returns True on exit code 0; the output is left in LastVerbOutput either way.
function RunCaptured(const CommandLine: string): Boolean;
var
  Rc: Integer;
  LogFile: string;
begin
  Result := False;
  LastVerbOutput := '';
  LogFile := ExpandConstant('{tmp}\setup-cmd.log');

  if not Exec(ExpandConstant('{sys}\cmd.exe'),
              '/C "' + CommandLine + ' > "' + LogFile + '" 2>&1"',
              ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, Rc) then
    Exit;

  LoadStringFromFile(LogFile, LastVerbOutput);
  DeleteFile(LogFile);
  Result := (Rc = 0);
end;

// True when C is a hexadecimal digit. Pascal Script has no character classes.
function IsHexDigit(C: Char): Boolean;
begin
  Result := ((C >= '0') and (C <= '9')) or ((C >= 'a') and (C <= 'f')) or ((C >= 'A') and (C <= 'F'));
end;

// The SHA-256 of a file, as certutil prints it, or '' when it cannot be read. Used to show a human the
// fingerprint of the authority they are about to trust.
//
// ⚠️ It scans for a run of 64 hex characters rather than reading a line by NUMBER, and rather than trusting
// certutil's wording. That output is localised -- a Tunisian clinic PC is frequently a French Windows -- and
// older builds space the digest into pairs, so both a line index and a label match would find nothing exactly
// where this matters most. Spaces are dropped before the scan for the same reason.
function FileFingerprint(const Path: string): string;
var
  Raw, Compact: string;
  I, Run, Start: Integer;
begin
  Result := '';
  if not RunCaptured('certutil -hashfile "' + Path + '" SHA256') then
    Exit;

  Raw := String(LastVerbOutput);
  Compact := '';
  for I := 1 to Length(Raw) do
    if IsHexDigit(Raw[I]) then
      Compact := Compact + Raw[I]
    else if (Raw[I] <> ' ') and (Raw[I] <> #13) and (Raw[I] <> #10) then
      Compact := Compact + '.';   // a separator, so a 64-run cannot span two different words

  Run := 0;
  Start := 0;
  for I := 1 to Length(Compact) do
  begin
    if Compact[I] = '.' then
      Run := 0
    else
    begin
      if Run = 0 then
        Start := I;
      Run := Run + 1;
      if Run = 64 then
      begin
        Result := Uppercase(Copy(Compact, Start, 64));
        Exit;
      end;
    end;
  end;
end;

// Fetch the clinic server's certificate authority and, with the operator's consent, trust it on this machine.
//
// ⚠️ THE FINGERPRINT PROMPT IS NOT CEREMONY. The CA is fetched over PLAIN HTTP -- it has to be, because the
// device cannot trust the HTTPS port until it holds this very file -- so anything on the path between this PC
// and the server could answer instead. Installing a certificate authority into the machine Root store is
// handing whatever holds its private key the ability to impersonate any site to this PC. The previous
// installer did it with `certutil -addstore -f` and no human ever saw a fingerprint.
//
// ⚠️ Its predecessor was worse than unverified: it shipped a `ca.crt` staged by hand from a server that did
// not exist at build time, so it imported NOTHING and every staff PC met a certificate warning instead.
function FetchAndTrustCa(const Host: string): Boolean;
var
  CaPath, Url, Fingerprint: string;
  Rc: Integer;
  Bytes: Int64;
begin
  Result := False;
  CaPath := ExpandConstant('{tmp}\ca.crt');
  Url := 'http://' + Host + ':{#TrustPort}/api/trust/ca.crt';

  try
    Bytes := DownloadTemporaryFile(Url, 'ca.crt', '', nil);
  except
    MsgBox('Impossible de récupérer le certificat du serveur « ' + Host + ' ».' + #13#10#13#10 +
           'Vérifiez que le serveur du cabinet est allumé, qu''il est sur le même réseau que ce PC,'
           + ' et que le nom saisi est correct.' + #13#10#13#10 +
           'Détail : ' + GetExceptionMessage, mbError, MB_OK);
    Exit;
  end;

  if not FileExists(CaPath) then
  begin
    MsgBox('Le certificat du serveur n''a pas pu être enregistré sur ce PC.', mbError, MB_OK);
    Exit;
  end;

  Fingerprint := FileFingerprint(CaPath);
  if Fingerprint = '' then
    Fingerprint := '(empreinte illisible)';

  if MsgBox('Ce PC va faire confiance au serveur « ' + Host + ' ».' + #13#10#13#10 +
            'Empreinte du certificat (SHA-256) :' + #13#10 +
            Fingerprint + #13#10#13#10 +
            'Elle doit correspondre à celle affichée sur le PC serveur'
            + ' (http://' + Host + ':{#TrustPort}/api/trust).' + #13#10#13#10 +
            'Faire confiance à ce serveur ?', mbConfirmation, MB_YESNO) = IDNO then
  begin
    MsgBox('Certificat refusé. Ce poste ne pourra pas se connecter au serveur tant que le certificat'
           + ' n''est pas installé.', mbInformation, MB_OK);
    Exit;
  end;

  if not RunWait(ExpandConstant('{sys}\certutil.exe'), '-addstore -f Root "' + CaPath + '"', '', Rc) then
  begin
    MsgBox('L''installation du certificat a échoué (code ' + IntToStr(Rc) + ').' + #13#10#13#10 +
           'Le navigateur affichera un avertissement de sécurité. Importez ca.crt manuellement dans'
           + ' « Autorités de certification racines de confiance » (voir README).', mbError, MB_OK);
    Exit;
  end;

  Result := True;
end;

// Fetch the shell's own setup from the server this poste was just pointed at, and run it.
//
// ⚠️ IT IS THE VELOPACK SETUP, NOT AN INNO ONE, and that is the whole reason this branch downloads rather
// than carrying a payload. `ShellUpdater.CheckAndStageAsync` gives up when `UpdateManager.IsInstalled` is
// false -- which is exactly what a shell installed into %ProgramFiles% by Inno is -- so the previous client
// installer produced a shell whose « Mettre à jour maintenant » did nothing for ever, silently. The API's
// `/api/meta/client-download` already prefers the Velopack package over the legacy Inno one when both are in
// the folder, so this needs nothing new on the server.
//
// ⚠️ Over HTTPS, and only AFTER the CA has been trusted. The other order fails on a certificate this PC does
// not yet recognise, which would read as « the server is unreachable ».
function FetchAndRunShellSetup(const Host: string): Boolean;
var
  SetupPath: string;
  Rc: Integer;
  Bytes: Int64;
begin
  Result := False;
  SetupPath := ExpandConstant('{tmp}\apexa-shell-setup.exe');

  try
    Bytes := DownloadTemporaryFile('https://' + Host + ':{#HttpsPort}/api/meta/client-download',
                                   'apexa-shell-setup.exe', '', nil);
  except
    MsgBox('Impossible de télécharger l''application depuis le serveur « ' + Host + ' ».' + #13#10#13#10 +
           'Le certificat est installé, mais le serveur n''a pas fourni de programme d''installation.'
           + ' Mettez à jour le serveur du cabinet, puis relancez cette installation.' + #13#10#13#10 +
           'Détail : ' + GetExceptionMessage, mbError, MB_OK);
    Exit;
  end;

  // /SILENT, not /VERYSILENT: Velopack's setup shows its own brief progress, and a wizard that appears to
  // hang for a 50 MB LAN transfer is how somebody reboots the PC halfway through.
  if not RunWait(SetupPath, '/SILENT', ExpandConstant('{tmp}'), Rc) then
  begin
    MsgBox('L''installation de l''application a échoué (code ' + IntToStr(Rc) + ').', mbError, MB_OK);
    Exit;
  end;

  Result := True;
end;

// The WebView2 runtime, without which the shell opens to a blank window.
//
// ⚠️ Present on Windows 11 and on Windows 10 21H2+, so this almost never fires -- which is exactly why it
// must not be a hard failure: refusing the install on the rare machine that lacks it, on a LAN with no
// internet, would strand the one PC this check exists to help. It says so instead.
procedure EnsureWebView2;
var
  Dummy: string;
  Rc: Integer;
  RuntimePath: string;
  Bytes: Int64;
begin
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Dummy)
     or RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Dummy)
     or RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Dummy) then
    Exit;

  RuntimePath := ExpandConstant('{tmp}\MicrosoftEdgeWebView2Setup.exe');
  try
    Bytes := DownloadTemporaryFile('https://go.microsoft.com/fwlink/p/?LinkId=2124703',
                                   'MicrosoftEdgeWebView2Setup.exe', '', nil);
    RunWait(RuntimePath, '/silent /install', ExpandConstant('{tmp}'), Rc);
  except
    MsgBox('Le composant Microsoft WebView2 est absent de ce PC et n''a pas pu être téléchargé'
           + ' (ce PC n''a peut-être pas d''accès à internet).' + #13#10#13#10 +
           'Installez « Microsoft Edge WebView2 Runtime » sur ce poste, puis relancez APEXA.',
           mbInformation, MB_OK);
  end;
end;

// Everything a workstation install does. No payload was copied; this is the whole of it.
procedure InstallWorkstation;
var
  Host: string;
begin
  Host := TypedServerAddress;

  if not FetchAndTrustCa(Host) then
    Exit;

  EnsureWebView2;

  if not FetchAndRunShellSetup(Host) then
    Exit;

  MsgBox('Ce poste est prêt.' + #13#10#13#10 +
         'APEXA s''ouvre depuis le menu Démarrer et se connecte à « ' + Host + ' ».' + #13#10#13#10 +
         'Les mises à jour se feront ensuite toutes seules, depuis le serveur du cabinet.',
         mbInformation, MB_OK);
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
  if CurStep <> ssPostInstall then
    Exit;

  // A workstation copied no payload, so none of the server work below applies to it: there is no API to run
  // a console verb from, no cluster to create and no service to register. It fetches trust and the app.
  if IsWorkstationRole then
  begin
    InstallWorkstation;
    Exit;
  end;

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

      // Last, and in this order: the machine has to be a working server before it is announced as one, and
      // it has to stay awake to remain one.
      DisableSleepOnMains;
      AnnounceServerAddress;
    end;
  end;
end;
