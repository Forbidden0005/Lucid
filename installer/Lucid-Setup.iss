; Lucid one-click setup shell.
;
; This script does NOT own install logic. It stages the release package payload
; to {tmp} and delegates to the tested installer\Install-Lucid.ps1 (versioned
; side-by-side layout, data migration, downgrade guard, shortcuts, current.json).
; Uninstall delegates to Uninstall-Lucid.ps1. Keeping one source of truth means
; zip-based installs, setup-exe installs, and future in-app updates all behave
; identically.
;
; Compile via scripts\build-setup-exe.ps1, which supplies these defines:
;   AppVersion      e.g. 0.1.0
;   Channel         e.g. preview
;   PayloadDir      extracted package root (contains app\, installer\, docs\)
;   OutputDir       where the setup exe is written
;   OutputBaseName  e.g. Lucid-Setup-0.1.0-preview

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z (use scripts\build-setup-exe.ps1)
#endif
#ifndef Channel
  #define Channel "preview"
#endif
#ifndef PayloadDir
  #error Pass /DPayloadDir=<extracted package root> (use scripts\build-setup-exe.ps1)
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef OutputBaseName
  #define OutputBaseName "Lucid-Setup-" + AppVersion
#endif

[Setup]
AppId={{6D1FA1E2-9C64-4F2B-8A57-3B0C7D5E41A9}
AppName=Lucid
AppVersion={#AppVersion}
AppVerName=Lucid {#AppVersion} ({#Channel})
AppPublisher=Lucid
DefaultDirName={localappdata}\Programs\Lucid
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Lucid
UninstallDisplayIcon={app}\{#AppVersion}\app\Lucid.App.exe
; The PowerShell installer owns the file layout; Inno only tracks its own
; uninstaller stub plus the persistent uninstall script below.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Package payload, staged to {tmp}; removed automatically after setup exits.
Source: "{#PayloadDir}\*"; DestDir: "{tmp}\payload"; Flags: recursesubdirs createallsubdirs deleteafterinstall
; Persistent copy of the uninstall script so [UninstallRun] has a stable,
; version-independent path (the versioned copies are deleted by uninstall itself).
Source: "{#PayloadDir}\installer\Uninstall-Lucid.ps1"; DestDir: "{app}"

[Run]
Filename: "{app}\{#AppVersion}\app\Lucid.App.exe"; Description: "{cm:LaunchProgram,Lucid}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Uninstall-Lucid.ps1"" -InstallRoot ""{app}"" -RemoveAllVersions"; Flags: runhidden waituntilterminated; RunOnceId: "LucidUninstall"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  Params: string;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    Params :=
      '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\payload\installer\Install-Lucid.ps1') + '"' +
      ' -PackageRoot "' + ExpandConstant('{tmp}\payload') + '"' +
      ' -InstallRoot "' + ExpandConstant('{app}') + '"' +
      ' -Force';
    if not WizardIsTaskSelected('desktopicon') then
      Params := Params + ' -NoDesktopShortcut';

    if not Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      SuppressibleMsgBox('Lucid install failed: PowerShell could not be started.', mbCriticalError, MB_OK, IDOK);
      Abort;
    end;

    if ResultCode <> 0 then
    begin
      SuppressibleMsgBox(
        'Lucid install failed (Install-Lucid.ps1 exit code ' + IntToStr(ResultCode) + ').' + #13#10 +
        'No changes were made outside ' + ExpandConstant('{app}') + '.',
        mbCriticalError, MB_OK, IDOK);
      Abort;
    end;
  end;
end;
