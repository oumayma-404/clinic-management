; ============================================================================================
; Clinic Management — Client Installer (Phase 5 / S7)
;
; A lightweight installer for staff PCs:
;   - places the thin WebView2 desktop shell with a Start-menu/taskbar shortcut (AC-2.1)
;   - imports the server's self-signed CA into the Windows Root store so the shell reaches the
;     server over HTTPS with no trust warning (FR-E2)
;
; The shell prompts for the server address on first launch (AC-2.2), so ONE client installer works
; for every clinic — nothing here is server-specific except the CA.
;
; R-1: committed-but-not-executed here. Build the payload with ..\publish-server.ps1 (which also
; publishes the shell) on an operator build machine, drop the server's ca.crt into
; build-output\client\ca\ca.crt, then compile with Inno Setup 6 (ISCC.exe). See ..\README.md.
; ============================================================================================

#define AppName      "Clinic Management"
#define AppVersion   "1.0.0"
#define AppPublisher "Clinic Management"
#define AppExe       "ClinicManagement.DesktopShell.exe"

[Setup]
AppId={{9B2E4C71-8A3F-4E15-B6D2-CLINICCLIENT01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Clinic Management Client
DefaultGroupName=Clinic Management
DisableProgramGroupPage=yes
OutputDir={#SourcePath}\..\build-output
OutputBaseFilename=ClinicManagementClientSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
; Admin is required to import the CA into the machine Root store. If a per-user install is preferred,
; import into CurrentUser\Root instead (see ImportCa) and drop PrivilegesRequired to lowest.

[Files]
Source: "{#SourcePath}\..\build-output\client\shell\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; The server's exported CA (build-output\client\ca\ca.crt). Optional at compile time so the shell can
; still be installed before the CA is available; a warning is shown if it is missing.
Source: "{#SourcePath}\..\build-output\client\ca\ca.crt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; WebView2 Evergreen runtime — OFFLINE standalone installer, dropped by publish-server.ps1's staging step
; into build-output\client\webview2\. Optional at compile time; if bundled it is run silently only when the
; runtime is missing (see EnsureWebView2 / Finding 16). Installed to {tmp} so it isn't left on disk.
Source: "{#SourcePath}\..\build-output\client\webview2\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le bureau"; GroupDescription: "Raccourcis :"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Lancer {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove the imported CA on uninstall (best-effort — matches by store; leaves other certs untouched).
; The subject CN must match the CA the server generates — "Clinic Management Local CA"
; (CertificateProvisioner.cs). A wrong name silently matches nothing, leaving the root CA permanently
; trusted after uninstall (Finding 3).
Filename: "{sys}\certutil.exe"; Parameters: "-delstore Root ""Clinic Management Local CA"""; Flags: runhidden; RunOnceId: "DelCa"

[Code]
const
  SW_HIDE = 0;

{ True if the WebView2 Evergreen runtime is already installed (machine-wide or per-user). Detected via the
  EdgeUpdate client key for the WebView2 runtime GUID; a present, non-zero 'pv' version means installed. }
function WebView2Installed: Boolean;
var
  Pv: string;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Pv) and (Pv <> '') and (Pv <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Pv) and (Pv <> '') and (Pv <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Pv) and (Pv <> '') and (Pv <> '0.0.0.0'));
end;

{ Ensure the WebView2 runtime is present (Finding 16). If missing and the offline standalone installer was
  bundled, run it silently; otherwise warn with a manual remedy. The WebView2 shell renders nothing without
  this runtime, and an offline LAN PC has no internet to fetch a bootstrapper. }
procedure EnsureWebView2;
var
  Rc: Integer;
  Boot: string;
begin
  if WebView2Installed then
    Exit;

  Boot := ExpandConstant('{tmp}\MicrosoftEdgeWebView2Setup.exe');
  if FileExists(Boot) then
  begin
    if not Exec(Boot, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, Rc) or (Rc <> 0) then
      MsgBox('L''installation du runtime WebView2 a échoué (code ' + IntToStr(Rc) + '). ' +
             'L''application ne s''affichera pas tant que « Microsoft Edge WebView2 Runtime » n''est pas ' +
             'installé (voir README).', mbError, MB_OK);
  end
  else
    MsgBox('Le runtime « Microsoft Edge WebView2 » est absent et son installateur hors-ligne n''a pas été ' +
           'fourni avec cet installateur. L''application ne s''affichera pas tant qu''il n''est pas installé. ' +
           'Installez MicrosoftEdgeWebView2Setup.exe (version « Evergreen Standalone ») sur ce poste (voir README).',
           mbError, MB_OK);
end;

{ Import the server CA into the machine Root store so the WebView2 shell trusts the server's
  self-signed HTTPS certificate (FR-E2). No-op with a warning if the CA was not bundled. }
procedure ImportCa;
var
  Rc: Integer;
  Ca: string;
begin
  Ca := ExpandConstant('{app}\ca.crt');
  if FileExists(Ca) then
  begin
    if not Exec(ExpandConstant('{sys}\certutil.exe'), '-addstore -f Root "' + Ca + '"',
                '', SW_HIDE, ewWaitUntilTerminated, Rc) or (Rc <> 0) then
      MsgBox('L''import du certificat CA a échoué (code ' + IntToStr(Rc) + '). ' +
             'Le navigateur affichera un avertissement de sécurité. Importez ca.crt manuellement ' +
             'dans « Autorités de certification racines de confiance » (voir README).',
             mbError, MB_OK);
  end
  else
    MsgBox('Aucun certificat CA (ca.crt) n''a été fourni avec cet installateur. ' +
           'Obtenez-le depuis le serveur (' + #39 + '%ProgramData%\ClinicManagement\ca.crt' + #39 + ') ' +
           'et importez-le dans le magasin racine, sinon la connexion HTTPS affichera un avertissement (voir README).',
           mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    EnsureWebView2;
    ImportCa;
  end;
end;
