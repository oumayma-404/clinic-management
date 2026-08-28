; ============================================================================================
; APEXA — Client Installer (Phase 5 / S7)
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

#define AppName      "APEXA"
#ifndef AppVersion
  ; The fallback for a hand-run `ISCC.exe this.iss`. `publish-server.ps1 -Version x.y.z` passes
  ; /DAppVersion and WINS over this — see ..\README.md § « Publier une mise a jour du shell ».
  ; It must stay in step with the shell assembly's own <Version>, because that assembly version is what
  ; the running shell reports as X-Client-Version and what the server's floor is compared against: ship an
  ; installer named 1.1.0 around a binary reporting 1.0.0 and your own floor refuses the build you just
  ; shipped, with nothing anywhere naming the mismatch.
  #define AppVersion   "1.0.0"
#endif
#define AppPublisher "APEXA"
#define AppExe       "ClinicManagement.DesktopShell.exe"
; The shell's own icon, read straight out of the repo at compile time. Generated from the one master
; (web/branding/icon.svg) by web/scripts/generate-icons.mjs -- the same file the .exe embeds, so the setup
; wizard, the Programs list and the running app cannot show three different marks.
#define AppIcon      SourcePath + "\..\..\desktop\ClinicManagement.DesktopShell\Assets\app.ico"

[Setup]
AppId={{9B2E4C71-8A3F-4E15-B6D2-CLINICCLIENT01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
; ⚠️ RENAMED WITH THE PRODUCT, reversing the note that used to sit here. That note argued the folder was never
; shown to anyone -- but DisableDirPage is not set, so Inno renders the destination page, and the folder appears
; a second time on the Ready-to-Install summary. A first-time APEXA user read the old product name twice during
; the wizard, which is the one surface this string was assumed not to have.
; The other half of that note was right and still applies: Inno keys an upgrade on AppId and reuses the RECORDED
; directory, so this only ever names a *fresh* install. Machines therefore diverge -- installs from this build
; land in \APEXA, ones made before it stay in \Clinic Management Client and are never moved. That is accepted:
; nothing resolves this path at runtime (the shell is self-contained and reads only %AppData%\ClinicManagement),
; and the uninstaller is registered against the recorded folder, not this constant.
DefaultDirName={autopf}\APEXA
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#SourcePath}\..\build-output
; The artifact filename stays ASCII and stays stable: it is typed into a URL, and StoreUrls:Windows points at it.
OutputBaseFilename=ClinicManagementClientSetup-{#AppVersion}
SetupIconFile={#AppIcon}
; What the Programs-and-Features row shows. Without it Windows renders a generic box beside the entry.
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
; Admin is required to import the CA into the machine Root store. If a per-user install is preferred,
; import into CurrentUser\Root instead (see ImportCa) and drop PrivilegesRequired to lowest.

[Files]
Source: "{#SourcePath}\..\build-output\client\shell\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; The server's exported CA (build-output\client\ca\ca.crt). Optional at compile time: a SelfHostedLan build
; stages it, a HostedMultiTenant build has no CA to stage. Absent is silent -- see ImportCa.
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
{ SW_HIDE is a built-in Inno Setup constant — do not redeclare it (duplicate-identifier compile error). }

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
  self-signed HTTPS certificate (FR-E2).

  ⚠️ ABSENT IS A VALID STATE, NOT A WARNING. One installer serves both topologies. A SelfHostedLan build
  always ships ca.crt (the operator stages it before ISCC runs), so a missing file means this is a
  HostedMultiTenant install -- where the server holds a publicly trusted certificate and there is nothing
  to import. The else-branch used to raise a French box telling the user to fetch a CA from « le serveur »,
  as the LAST screen of the wizard, for a clinic that has no server of its own. Silence is the correct
  behaviour there; a LAN build that genuinely lost its CA still surfaces as a browser warning on first
  connect, which is the symptom the operator can act on. }
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
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    EnsureWebView2;
    ImportCa;
  end;
end;
