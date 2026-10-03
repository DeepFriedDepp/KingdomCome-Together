; Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
; GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
; content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
; Kingdom Come: Together -- one-click installer (WO-134: the new name; before, "KCD2 Multiplayer").
;
; Compile with tools\Build-Installer.ps1, not by hand: this script installs
; the *output* of tools\Publish-Release.ps1 (release\KCDMP\) and will refuse
; to compile if that folder is not there.
;
; What it does that a plain file-copy installer does not:
;   * finds Steam, the game, the KCD2 Modding Tools and their workspace with
;     the launcher's own detection code (WO-150: KcdMpSetup.exe, see
;     SteamDetect.iss), so the two can never disagree,
;   * deploys the game mod into <ModdingTools>\Mods\kdcmp when the Modding
;     Tools are installed AND set up; otherwise installs everything else and
;     leaves the rest to the launcher's checklist (WO-150: no dead end),
;   * pre-seeds the launcher's settings.json with the game path it found, so
;     first launch needs zero configuration (WO-154: only when there is no
;     settings.json yet -- the player's file is never written by Setup),
;   * installs the WebView2 runtime when missing, which the Photino-based
;     launcher cannot render without.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

; WO-134: the name players see. The colon cannot be in a file name, so shortcuts and
; the Start menu folder use ShortcutName. Never renamed (installs, saves and upgrades
; depend on them): the AppId below, the install folder {localappdata}\KCDMP, the
; HKCU\Software\KCDMP key, the kdcmp mod folder, the launcher's exe name.
#define AppName "Kingdom Come: Together"
#define ShortcutName "Kingdom Come Together"
#define OldShortcutName "KCD2 Multiplayer"
#define Disclaimer "Unofficial. Not affiliated with or endorsed by Warhorse Studios."
#define AppExeName "KCDMP_launcher.exe"
#define AppPublisher "Kingdom Come: Together contributors"
#define AppUrl "https://github.com/DeepFriedDepp/KingdomCome-Together"

; ModdingToolsAppId is defined by SteamDetect.iss, included at the top of
; [Code] below, alongside the evidence it was read from.

; Evergreen WebView2 Runtime bootstrapper. Microsoft's documented permanent
; redirect; ~2 MB, and it installs per-user when Setup is not elevated, which
; is exactly our case (PrivilegesRequired=lowest).
#define WebView2BootstrapUrl "https://go.microsoft.com/fwlink/p/?LinkId=2124703"

[Setup]
AppId={{88C5B9F1-0E71-4D60-9418-5575D5684F95}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}

; %LocalAppData%, deliberately, and PrivilegesRequired=lowest with it:
;   * the launcher writes settings.json in its own working directory
;     (KCDMP_launcher/Pages/Home.razor.cs:33), so a Program Files install
;     would need elevation every time the user pressed Save in Settings;
;   * the mod's target folder is inside the Steam library, and Steam grants
;     BUILTIN\Users FullControl on its own tree, so an unelevated install can
;     write <ModdingTools>\Mods even when Steam sits in Program Files;
;   * a non-technical friend never sees a UAC prompt.
DefaultDirName={localappdata}\KCDMP
PrivilegesRequired=lowest
UsePreviousAppDir=yes

DefaultGroupName={#ShortcutName}
; WO-134: an upgrade must use the NEW folder name, not the one the old install chose.
UsePreviousGroup=no
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\release
OutputBaseFilename=KingdomComeTogether-Setup-{#AppVersion}
; WO-134: the installer's own icon (the launcher's multi-size .ico, built by tools\Build-Branding.py)
SetupIconFile=..\KCDMP_launcher\app.ico
; WO-134: the exe keeps its path across the rename, so Windows' icon cache kept showing the
; old logo on the upgraded shortcuts (maintainer's report). The shortcuts name app.ico
; directly (a path the cache has not seen), and Setup tells Explorer to refresh icons at
; the end (ChangesAssociations = SHChangeNotify(SHCNE_ASSOCCHANGED); nothing is registered).
ChangesAssociations=yes
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupLogging=yes
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
; WO-134: the disclaimer on every renamed surface.
WelcomeLabel2=This will install [name/ver] on your computer.%n%n{#Disclaimer}%n%nIt is recommended that you close the game, the launcher and any other applications before continuing.
FinishedLabel=Setup has finished installing [name] on your computer. The application may be launched by selecting the installed shortcuts.%n%n{#Disclaimer}

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; The whole Publish-Release.ps1 output: launcher, agent, relay, native DLL,
; injector, and the self-contained .NET runtime beside them. The launcher
; resolves relative DllPath/AgentPath/RelayPath against its own directory,
; so this flat layout is the one AppSettings' defaults already expect.
;
; overwritereadonly (WO-74) is not decoration. Without it, ONE read-only file
; anywhere in the install directory stops the entire install dead: Inno raises
; "The existing file could not be replaced because it is marked read-only" as
; an Abort/Retry/Ignore box, /SUPPRESSMSGBOXES answers Abort, and Setup exits
; 5 -- the same code as a user pressing Cancel. Reproduced on 2026-08-28
; against Setup 0.18.8: the abort landed on appsettings.json, the third file
; alphabetically, so every file after it kept its old build, and the mod
; folder had already been emptied by PrepareToInstall. A read-only attribute
; is not exotic: restores from backup, copies off read-only media, and some
; antivirus quarantine-restores all set it.
Source: "..\release\KCDMP\*"; DestDir: "{app}"; Flags: ignoreversion overwritereadonly recursesubdirs createallsubdirs

; The game mod, and ONLY these two files. Do not turn this back into a
; wildcard over kdcmp\.
;
; kdcmp\Data\ also holds the pak's *sources* -- Libs\Tables\...,
; Scripts\Startup\kdcmp.lua -- which tools\Build-And-Install-
; Mod.ps1 packs into kdcmp.pak and deliberately does not copy. Deploying them
; loose as well breaks the game outright: a loose Data\Libs\Tables directory
; inside a mod takes over the engine's table root, and every base table then
; fails to resolve. The game dies at startup with "114 tables are not loaded",
; and nothing in that message points at this. Observed for real on 2026-07-30.
;
; uninsneveruninstall is deliberate and load-bearing too: without it the
; uninstaller silently deletes these files out of the player's game folder,
; which is not ours to do unasked. Removal is handled by the explicit
; question in CurUninstallStepChanged instead.
;
; WO-150: only when the Modding Tools are installed AND their workspace is
; linked (Check: ShouldPlaceMod). Otherwise these two are held back: the
; payload's staged copy (<app>\mod\kdcmp, from Build-Installer.ps1) is what
; the launcher places after its checklist has linked the game's files.
Source: "..\kdcmp\mod.manifest"; DestDir: "{code:GetKdcmpTargetDir}"; Flags: ignoreversion overwritereadonly uninsneveruninstall; Check: ShouldPlaceMod
Source: "..\kdcmp\Data\kdcmp.pak"; DestDir: "{code:GetKdcmpTargetDir}\Data"; Flags: ignoreversion overwritereadonly uninsneveruninstall; Check: ShouldPlaceMod

; WO-150: the setup helper, extracted to the temporary folder before anything is installed
; (detection on the wizard's first page). It is also installed into the app folder by
; the wildcard above, for the launcher's one UAC step.
Source: "..\release\KCDMP\KcdMpSetup.exe"; Flags: dontcopy

[Icons]
; WorkingDir matters and is not decoration: settings.json is a bare relative
; filename in the launcher, so it lands wherever the process was started from.
Name: "{group}\{#ShortcutName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"; Comment: "{#Disclaimer}"
Name: "{group}\Uninstall {#ShortcutName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#ShortcutName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon; Comment: "{#Disclaimer}"

[InstallDelete]
; WO-134: an upgrade from a build before the rename replaces its shortcuts, so the
; Start menu and the desktop never show both names. Only the old shortcut files and
; the old folder once it is empty -- nothing else there is ours.
Type: files; Name: "{autoprograms}\{#OldShortcutName}\{#OldShortcutName}.lnk"
Type: files; Name: "{autoprograms}\{#OldShortcutName}\Uninstall {#OldShortcutName}.lnk"
Type: dirifempty; Name: "{autoprograms}\{#OldShortcutName}"
Type: files; Name: "{autodesktop}\{#OldShortcutName}.lnk"

[Registry]
; Not settings -- just enough for the uninstaller to find the mod it deployed
; into someone else's game folder, and for a later Setup to recognise its own
; earlier deployment instead of treating it as a foreign kdcmp.
; Listed first so it is *undone* last (uninstall replays this section in
; reverse): uninsdeletekeyifempty on a later line ran while the other values
; were still present and left an orphan key behind.
Root: HKCU; Subkey: "Software\KCDMP"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\KCDMP"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"
; WO-150: written only when known/placed; the launcher writes both itself once its
; checklist places a held-back mod (Home.Wo150.cs RecordModsPath).
Root: HKCU; Subkey: "Software\KCDMP"; ValueType: string; ValueName: "GamePath"; ValueData: "{code:GetDetectedGameExe}"; Check: HaveGameExe
Root: HKCU; Subkey: "Software\KCDMP"; ValueType: string; ValueName: "ModsPath"; ValueData: "{code:GetKdcmpTargetDir}"; Check: ShouldPlaceMod

[Run]
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; settings.json, favourites and the native plugin's log are created at
; runtime, not by [Files], so nothing else would clean them up.
Type: filesandordirs; Name: "{app}"

[Code]
#include "SteamDetect.iss"

var
  GamePage: TWizardPage;
  GameStatusLabel: TNewStaticText;
  GamePathLabel: TNewStaticText;
  GameHelpLabel: TNewStaticText;
  RecheckButton: TNewButton;
  BrowseButton: TNewButton;

  DetectedGameExe: String;
  DetectedGameRoot: String;
  SteamFound: Boolean;
  ModdingToolsRegistered: Boolean;
  // WO-150: the shared detection's verdict. True only when the Modding Tools are
  // installed and their workspace linked; otherwise the mod is held back.
  PlaceMod: Boolean;
  DetectReason: String;
  DetectWorked: Boolean;
  BrowsedExe: String;

  // WO-74 -- post-install verification state. See VerifyInstalledFiles.
  // (Line comments, not brace ones: an Inno constant in braces inside a brace
  // comment closes the comment early -- Pascal comments do not nest.)
  ManApp: TArrayOfString;      // 'rel|size|sha256' per file in the install dir
  ManMod: TArrayOfString;      // same, for the two files in the game folder
  AppIndex: String;            // '|rel|rel|...|', lowercased -- the sweep's lookup
  VerifyFailed: Boolean;

{ The only way to hand a caller a non-zero exit code from a Setup that
  otherwise "succeeded". Inno finishes normally after ssPostInstall and has no
  API for this, and the alternative -- raising an exception in ssPostInstall --
  triggers a rollback that uninstalls the files we just verified as good.
  Cost of doing it this way: Setup's own temp folder is not cleaned up on
  this path. That is a leaked temp folder on an install that already failed, in
  exchange for automation being able to see that it failed at all. }
procedure ExitProcess(uExitCode: UINT); external 'ExitProcess@kernel32.dll stdcall';

{ Pascal Script has no attribute API of its own. Clearing read-only before a
  delete is the same defence overwritereadonly gives the file copy -- see the
  Files-section comment for what one read-only file did to a whole install. }
const
  FILE_ATTR_NORMAL = $00000080;

function SetFileAttributesW(lpFileName: String; dwFileAttributes: LongInt): Boolean;
  external 'SetFileAttributesW@kernel32.dll stdcall';

procedure ClearReadOnly(const Path: String);
begin
  SetFileAttributesW(Path, FILE_ATTR_NORMAL);
end;

{ -------------------------------------------------------------- WebView2 }

function WebView2Installed(): Boolean;
var
  V: String;
begin
  Result := False;
  { Per-machine install writes the 32-bit view; per-user writes HKCU. Both are
    the documented locations, and the 32-bit one is where this dev machine's
    runtime (pv 150.0.4078.105) actually is. }
  if RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', V) then
    if (V <> '') and (V <> '0.0.0.0') then Result := True;
  if not Result then
    if RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', V) then
      if (V <> '') and (V <> '0.0.0.0') then Result := True;
  if not Result then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', V) then
      if (V <> '') and (V <> '0.0.0.0') then Result := True;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if not WizardSilent then
  begin
    if ProgressMax > 0 then
      WizardForm.PreparingLabel.Caption :=
        'Downloading the Microsoft WebView2 runtime (' +
        IntToStr(Progress div 1024) + ' of ' + IntToStr(ProgressMax div 1024) + ' KB)...'
    else
      WizardForm.PreparingLabel.Caption :=
        'Downloading the Microsoft WebView2 runtime (' + IntToStr(Progress div 1024) + ' KB)...';
  end;
  Result := True;
end;

{ Returns '' on success, or a message explaining why setup cannot continue. }
function EnsureWebView2(): String;
var
  Bootstrapper: String;
  ResultCode: Integer;
begin
  Result := '';
  if WebView2Installed() then Exit;

  if not WizardSilent then
    WizardForm.PreparingLabel.Caption := 'Downloading the Microsoft WebView2 runtime...';

  Bootstrapper := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
  try
    DownloadTemporaryFile('{#WebView2BootstrapUrl}', 'MicrosoftEdgeWebview2Setup.exe', '', @OnDownloadProgress);
  except
    Result :=
      'The launcher needs the Microsoft WebView2 runtime, and it could not be downloaded:' + #13#10 +
      GetExceptionMessage + #13#10#13#10 +
      'Check your internet connection and try again, or install "Microsoft Edge WebView2 Runtime"' + #13#10 +
      'from microsoft.com and re-run this installer.';
    Exit;
  end;

  if not WizardSilent then
    WizardForm.PreparingLabel.Caption := 'Installing the Microsoft WebView2 runtime...';

  if not Exec(Bootstrapper, '/silent /install', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'The WebView2 runtime installer could not be started (error ' + IntToStr(ResultCode) + ').';
    Exit;
  end;

  if not WebView2Installed() then
    Result :=
      'The WebView2 runtime installer finished (exit code ' + IntToStr(ResultCode) + ') but the' + #13#10 +
      'runtime is still not registered. The launcher cannot draw its window without it.' + #13#10#13#10 +
      'Install "Microsoft Edge WebView2 Runtime" from microsoft.com, then run this installer again.';
end;

{ ------------------------------------------------------------- mod folder }

function ModsDir(): String;
begin
  if DetectedGameRoot = '' then
    Result := ''
  else
    Result := DetectedGameRoot + '\Mods';
end;

function GetKdcmpTargetDir(Param: String): String;
begin
  Result := ModsDir();
  if Result <> '' then
    Result := Result + '\kdcmp';
end;

function GetDetectedGameExe(Param: String): String;
begin
  Result := DetectedGameExe;
end;

{ WO-150 -- [Files]/[Registry] Check functions. }
function ShouldPlaceMod(): Boolean;
begin
  Result := PlaceMod and (DetectedGameRoot <> '');
end;

function HaveGameExe(): Boolean;
begin
  Result := DetectedGameExe <> '';
end;

{ DelTree is the normal path, but it was observed failing once on this folder
  while a handle was still open on it -- the same thing an Explorer window, a
  running game or an antivirus scan will do on a user's machine. So: try,
  give the handle a moment to close, try again, then fall back to rd, and
  judge the outcome by whether the folder is actually gone rather than by the
  return value. }
function RemoveModFolder(const Target: String): Boolean;
var
  ResultCode: Integer;
begin
  if DelTree(Target, True, True, True) and (not DirExists(Target)) then
  begin
    Result := True;
    Exit;
  end;

  Sleep(750);
  DelTree(Target, True, True, True);
  if not DirExists(Target) then
  begin
    Result := True;
    Exit;
  end;

  Exec(ExpandConstant('{cmd}'), '/c rd /s /q "' + Target + '"', '', SW_HIDE,
       ewWaitUntilTerminated, ResultCode);
  Result := not DirExists(Target);
end;

{ WO-74. What RemoveModFolder used to be called for on every upgrade, and why
  it is not any more.

  PrepareToInstall deleted the whole mod folder before the file copy ran.
  When that copy then aborted -- one read-only file in the install dir was enough, see
  the Files-section comment -- the player was left with NO mod in their game
  at all, from an installer that reported nothing but a cancel. Observed on
  2026-08-28 against Setup 0.18.8. (A line here may not start with a square
  bracket, even inside a comment: ISCC reads it as a section tag.)

  So for our own upgrades: prune instead of delete. Everything that is not
  one of the two files we own goes (that is what the delete was really for --
  clearing loose pak *sources*, a stray Data\Libs\Tables being the failure
  that started it), and the two we own are left in place for [Files] to
  overwrite. An install that dies half way now leaves the previous version's
  mod working rather than no mod at all.

  A foreign kdcmp still gets the full RemoveModFolder after the explicit ask:
  keeping half of somebody else's mod would be worse than replacing it. }
procedure PruneDirTo(const Dir: String; const KeepFile, KeepFile2: String);
var
  FindRec: TFindRec;
  Full: String;
begin
  if not DirExists(Dir) then Exit;
  if FindFirst(Dir + '\*', FindRec) then
  try
    repeat
      if (FindRec.Name = '.') or (FindRec.Name = '..') then Continue;
      Full := Dir + '\' + FindRec.Name;
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        DelTree(Full, True, True, True)
      else if (CompareText(FindRec.Name, KeepFile) <> 0) and (CompareText(FindRec.Name, KeepFile2) <> 0) then
      begin
        { Clear read-only first, for the same reason [Files] carries
          overwritereadonly: a flagged file must not stop the install. }
        ClearReadOnly(Full);
        DeleteFile(Full);
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

{ True when the folder now holds nothing but (at most) our own two files. }
function PruneModFolder(const Target: String): Boolean;
var
  FindRec: TFindRec;
  Full: String;
begin
  Result := True;
  if not DirExists(Target) then Exit;

  if FindFirst(Target + '\*', FindRec) then
  try
    repeat
      if (FindRec.Name = '.') or (FindRec.Name = '..') then Continue;
      Full := Target + '\' + FindRec.Name;
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        if CompareText(FindRec.Name, 'Data') = 0 then
          { WO-148: kdcmp_keys.pak is ours too (built here from the game's own files); kept so a
            failed rebuild after the copy leaves the previous keys working. }
          PruneDirTo(Full, 'kdcmp.pak', 'kdcmp_keys.pak')
        else
          DelTree(Full, True, True, True);
      end
      else if CompareText(FindRec.Name, 'mod.manifest') <> 0 then
      begin
        ClearReadOnly(Full);
        DeleteFile(Full);
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;

  { The two we own are about to be overwritten by [Files]; clear read-only on
    them too so that overwrite cannot be the thing that fails. }
  if FileExists(Target + '\mod.manifest') then
    ClearReadOnly(Target + '\mod.manifest');
  if FileExists(Target + '\Data\kdcmp.pak') then
    ClearReadOnly(Target + '\Data\kdcmp.pak');
end;

{ -------------------------------------------------------------- the gate }

procedure UpdateGamePage();
begin
  { WO-150: this page informs; it no longer gates. Whatever is missing, Setup
    installs the launcher, and the launcher's checklist finishes the rest --
    so a new player is never stopped here. }
  if ShouldPlaceMod() then
  begin
    GameStatusLabel.Caption := 'Modding Tools found and set up.';
    GamePathLabel.Caption := DetectedGameExe;
    GameHelpLabel.Caption :=
      'The mod will be installed into:' + #13#10 +
      GetKdcmpTargetDir('') + #13#10#13#10 +
      'If this is not the copy you want to use, click Browse and pick the' + #13#10 +
      'KingdomCome.exe of your Modding Tools install.';
  end
  else
  begin
    if DetectedGameExe <> '' then
    begin
      GameStatusLabel.Caption := 'Modding Tools found, but not set up yet.';
      GamePathLabel.Caption := DetectedGameExe;
    end
    else if not SteamFound then
    begin
      GameStatusLabel.Caption := 'Steam was not found on this PC.';
      GamePathLabel.Caption := '(the launcher will walk you through it)';
    end
    else if ModdingToolsRegistered then
    begin
      GameStatusLabel.Caption := 'The Modding Tools are listed in Steam, but their files are missing.';
      GamePathLabel.Caption := '(a download still running, or one that was cancelled)';
    end
    else
    begin
      GameStatusLabel.Caption := 'The KCD2 Modding Tools are not installed yet.';
      GamePathLabel.Caption := '(the launcher will install them through Steam)';
    end;
    GameHelpLabel.Caption :=
      DetectReason + #13#10#13#10 +
      'You can continue now. When Setup finishes, open the launcher: it shows a' + #13#10 +
      'checklist and does each step in the background, so you can keep using' + #13#10 +
      'your PC while Steam downloads.';
  end;
end;

procedure RefreshDetection();
begin
  { /STEAMROOT=<dir> makes detection read a fixture tree instead of the real
    Steam install. This exists so the installer's cases can be exercised
    without touching real Steam metadata -- renaming a live appmanifest to
    fake "not installed" costs a multi-gigabyte redownload, because Steam
    treats the missing manifest as "not installed" and drops the app's
    entitlement, and the game then refuses to start with a licence error.
    Learned the expensive way. See docs\INSTALLER-TESTING.md. A path that
    does not exist reproduces "Steam is not installed". }
  DetectWorked := RunDetect(ExpandConstant('{param:steamroot|}'), BrowsedExe);
  SteamFound := DetectValue('steam_found') = '1';
  ModdingToolsRegistered := DetectValue('mt_registered') = '1';
  DetectedGameExe := DetectValue('mt_exe');
  DetectedGameRoot := DetectValue('mt_root');
  PlaceMod := DetectValue('place_mod') = '1';
  DetectReason := DetectValue('reason');
  if DetectedGameRoot = '' then
    DetectedGameExe := '';
  if not DetectWorked then
    DetectReason := 'Setup could not check your Steam library. The launcher will check it again and finish setup.';
end;

procedure RecheckClick(Sender: TObject);
begin
  RefreshDetection();
  UpdateGamePage();
end;

procedure BrowseClick(Sender: TObject);
var
  FileName, Root: String;
begin
  FileName := DetectedGameExe;
  if not GetOpenFileName('Select the Modding Tools KingdomCome.exe', FileName, '',
                         'KingdomCome.exe|KingdomCome.exe|All files|*.*', 'exe') then
    Exit;

  if not CheckModdingToolsExe(FileName, Root) then
  begin
    MsgBox('That is not the Modding Tools build.' + #13#10#13#10 +
           'Framework.dll and CrySystem.dll are not next to that executable, which means it is the' + #13#10 +
           'retail game -- the mod has nothing to hook into there.',
           mbError, MB_OK);
    RefreshDetection();
    Exit;
  end;

  BrowsedExe := FileName;
  RefreshDetection();
  UpdateGamePage();
end;

{ ------------------------------------------------------------ wizard flow }

procedure InitializeWizard();
begin
  GamePage := CreateCustomPage(wpLicense, 'Kingdom Come: Deliverance II Modding Tools',
                               'The mod runs on the Modding Tools build of the game.');

  GameStatusLabel := TNewStaticText.Create(WizardForm);
  GameStatusLabel.Parent := GamePage.Surface;
  GameStatusLabel.Left := 0;
  GameStatusLabel.Top := 0;
  GameStatusLabel.Width := GamePage.SurfaceWidth;
  GameStatusLabel.Font.Style := [fsBold];

  GamePathLabel := TNewStaticText.Create(WizardForm);
  GamePathLabel.Parent := GamePage.Surface;
  GamePathLabel.Left := 0;
  GamePathLabel.Top := ScaleY(18);
  GamePathLabel.Width := GamePage.SurfaceWidth;
  GamePathLabel.AutoSize := False;
  GamePathLabel.Height := ScaleY(28);
  GamePathLabel.WordWrap := True;

  GameHelpLabel := TNewStaticText.Create(WizardForm);
  GameHelpLabel.Parent := GamePage.Surface;
  GameHelpLabel.Left := 0;
  GameHelpLabel.Top := ScaleY(54);
  GameHelpLabel.Width := GamePage.SurfaceWidth;
  GameHelpLabel.AutoSize := False;
  GameHelpLabel.Height := GamePage.SurfaceHeight - ScaleY(96);
  GameHelpLabel.WordWrap := True;

  RecheckButton := TNewButton.Create(WizardForm);
  RecheckButton.Parent := GamePage.Surface;
  RecheckButton.Left := 0;
  RecheckButton.Top := GamePage.SurfaceHeight - ScaleY(26);
  RecheckButton.Width := ScaleX(90);
  RecheckButton.Height := ScaleY(24);
  RecheckButton.Caption := 'Re-check';
  RecheckButton.OnClick := @RecheckClick;

  BrowseButton := TNewButton.Create(WizardForm);
  BrowseButton.Parent := GamePage.Surface;
  BrowseButton.Left := GamePage.SurfaceWidth - ScaleX(90);
  BrowseButton.Top := RecheckButton.Top;
  BrowseButton.Width := ScaleX(90);
  BrowseButton.Height := ScaleY(24);
  BrowseButton.Caption := 'Browse...';
  BrowseButton.OnClick := @BrowseClick;

  RefreshDetection();
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = GamePage.ID then
    UpdateGamePage()
  else if CurPageID = wpFinished then
  begin
    if ShouldPlaceMod() then
      WizardForm.FinishedLabel.Caption :=
        'Kingdom Come: Together is installed and already knows where your game is.' + #13#10#13#10 +
        'To play together, one of you clicks HOST GAME and shares the address the' + #13#10 +
        'launcher shows; everyone else adds that address under Join. Same house is' + #13#10 +
        'enough on its own -- for playing across the internet see docs/NETWORKING.md' + #13#10 +
        'in the project repository.'
    else
      { WO-150: the hand-off. The launcher's checklist installs what is missing
        through Steam, links the workspace and places the mod; a Steam download
        can take an hour, so none of it happens here. }
      WizardForm.FinishedLabel.Caption :=
        'Kingdom Come: Together is installed. One more thing before you can play:' + #13#10#13#10 +
        'open the launcher (leave the box below ticked). It shows a short checklist' + #13#10 +
        'and finishes setup for you -- the Modding Tools from Steam, linking the' + #13#10 +
        'game''s files, and the mod itself -- step by step, in the background.' + #13#10#13#10 +
        'You can keep using your PC meanwhile, and close the launcher at any time:' + #13#10 +
        'it carries on where it stopped the next time you open it.';
  end;
end;

{ Kills this project's own processes (never the game). Shared by the silent
  install gate below and by silent uninstall -- unattended means unattended,
  and the launcher only writes settings.json when the user presses Save, so
  there is no in-flight state to lose. }
procedure KillOursQuietly();
var
  I, ResultCode: Integer;
  Names: array[0..3] of String;
begin
  Names[0] := 'KCDMP_launcher.exe';
  Names[1] := 'KcdMpClient.exe';
  Names[2] := 'KcdMpServer.exe';
  { WO-74: the launcher starts this one itself (WO-35), it runs out of
    the MasterServer subfolder of the install dir, and it was in none of the three process lists. Inno's
    RestartManager does close it in practice -- observed on 2026-08-28 -- but
    RM is a courtesy, not a guarantee: it cannot reach a process in another
    session, and a process is free to ignore the shutdown request. }
  Names[3] := 'KcdMpMasterServer.exe';
  for I := 0 to 3 do
    Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im "' + Names[I] + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ WO-32 follow-up -- the install-time process gate.

  A real 0.11.8 install half-applied: Setup ran while agent/relay processes
  were alive, CloseApplications did not catch them (they were running from a
  dev tree rather than the install directory, but the DLLs still failed to
  be overwritten, without an error), and the result was a
  launcher on the new build with an agent on the old one -- the newly-shipped
  feature silently inert. See docs/WO-32-findings.md. So: refuse to install
  while ANY of this project's processes, or the game, is running, no matter
  where they run from. Interactive gets Retry/Cancel; silent kills our own
  processes (mirroring InitializeUninstall's documented behaviour) but never
  the game -- an unattended install has no business closing someone's game. }
function FirstInstallBlocker(): String;
var
  I, ResultCode: Integer;
  Names: array[0..4] of String;
begin
  Result := '';
  Names[0] := 'KCDMP_launcher.exe';
  Names[1] := 'KcdMpClient.exe';
  Names[2] := 'KcdMpServer.exe';
  Names[3] := 'KcdMpMasterServer.exe';   { WO-74 -- see KillOursQuietly }
  Names[4] := 'KingdomCome.exe';

  for I := 0 to 4 do
    if Exec(ExpandConstant('{cmd}'),
            '/c tasklist /fi "imagename eq ' + Names[I] + '" /nh | find /i "' + Names[I] + '"',
            '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      if ResultCode = 0 then
      begin
        Result := Names[I];
        Exit;
      end;
end;

{ Returns '' when clear to install, or an abort message. }
function EnsureNothingRunning(): String;
var
  Blocker: String;
begin
  Result := '';

  Blocker := FirstInstallBlocker();
  while Blocker <> '' do
  begin
    if WizardSilent then
    begin
      if Blocker = 'KingdomCome.exe' then
      begin
        Result := 'The game (KingdomCome.exe) is running. Setup cannot safely replace files while it is. Close the game and run Setup again.';
        Exit;
      end;
      KillOursQuietly();
      Sleep(1500);
      Blocker := FirstInstallBlocker();
      if Blocker <> '' then
      begin
        Result := Blocker + ' is still running and could not be closed. Close it and run Setup again.';
        Exit;
      end;
      Break;
    end;

    { No hard line breaks inside sentences: MsgBox uses a proportional font
      and wraps on its own; manual mid-sentence breaks made the real dialog
      ragged (seen on screen). Breaks only between paragraphs. }
    if MsgBox(Blocker + ' is still running.' + #13#10#13#10 +
              'Installing over running programs is how an update half-applies: some files update, the ones in use silently do not, and the result looks installed but is a mix of two versions.' + #13#10#13#10 +
              'Close it (launcher, agent, relay, and the game), then click Retry.',
              mbConfirmation, MB_RETRYCANCEL) <> IDRETRY then
    begin
      Result := 'Setup was cancelled because ' + Blocker + ' was still running.';
      Exit;
    end;
    Blocker := FirstInstallBlocker();
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Target: String;
begin
  Result := '';

  { WO-150: no refusal here any more, also under /VERYSILENT. Without set-up
    Modding Tools the launcher, agent and the rest still install (with the
    mod staged in the app folder), and the launcher's checklist finishes. }
  if ShouldPlaceMod() then
    Log('install: the Modding Tools are set up; the mod goes into ' + GetKdcmpTargetDir(''))
  else
    Log('install: the mod is held back for the launcher to place (' + DetectReason + ')');

  { The process gate, before anything is written -- see its comment above. }
  Result := EnsureNothingRunning();
  if Result <> '' then Exit;

  Result := EnsureWebView2();
  if Result <> '' then Exit;

  if not ShouldPlaceMod() then Exit;

  Target := GetKdcmpTargetDir('');
  if DirExists(Target) then
  begin
    { Our own marker means this is an upgrade; anything else is somebody
      else's kdcmp and gets an explicit ask before it is replaced. }
    if not RegValueExists(HKCU, 'Software\KCDMP', 'ModsPath') then
    begin
      if not WizardSilent then
        if MsgBox('There is already a mod folder at:' + #13#10#13#10 + Target + #13#10#13#10 +
                  'It was not put there by this installer. Replace it?',
                  mbConfirmation, MB_YESNO) <> IDYES then
        begin
          Result := 'Setup was cancelled so the existing mod folder could be kept.';
          Exit;
        end;

      if not RemoveModFolder(Target) then
      begin
        Result := 'The existing mod folder could not be removed:' + #13#10 + Target + #13#10#13#10 +
                  'Close the game (and any Explorer window showing that folder) and try again.';
        Exit;
      end;
    end
    else
      { Ours: prune, do not delete -- see PruneModFolder. }
      PruneModFolder(Target);
  end;

  if not ForceDirectories(ModsDir()) then
    Result := 'The game''s Mods folder could not be created:' + #13#10 + ModsDir();
end;

{ Pre-seed settings.json so the first launch needs no trip to Settings.
  Only GamePath is written: every other field in AppSettings has a usable C#
  default, and a partial file deserialises to exactly those defaults.

  WO-154: only when there is no settings.json at all. The file is the
  player's: an existing one is never written, whatever it holds -- until
  0.44.0 a file whose GamePath was "" was replaced by the seed, and every
  other setting in it was lost on upgrade. An empty GamePath is filled by the
  launcher instead, as that one key (Home.Wo150.cs AdoptDetectedGamePath). }
procedure SeedSettings();
var
  Path, Json, Escaped: String;
  Lines: TArrayOfString;
begin
  Path := ExpandConstant('{app}\settings.json');
  // WO-150: nothing found yet -- the launcher fills GamePath once its checklist finds the Modding Tools.
  if DetectedGameExe = '' then Exit;
  if FileExists(Path) then Exit;

  Escaped := DetectedGameExe;
  StringChangeEx(Escaped, '\', '\\', True);

  Json := '{"GamePath":"' + Escaped + '"}';
  SetArrayLength(Lines, 1);
  Lines[0] := Json;
  SaveStringsToUTF8FileWithoutBOM(Path, Lines, False);
end;

{ ================================================ verify, sweep and repair }

{ WO-32 follow-up, rebuilt in WO-74 -- the install proves itself before
  declaring success, and repairs what it can before judging.

  Build-Installer.ps1 writes install-manifest.txt into the payload after
  publish (APP|<rel>|<size>|<sha256> for the install dir, MOD|... for the two files in
  the game folder), so it ships inside the install directory and describes
  exactly what this Setup carried.

  Three things changed in WO-74, each closing a way the old check could
  report PASS over a broken install:

    * sha256, not just size. A stale file that happened to match on length
      passed. "Every stale-vs-built pair observed differed in size" was luck.
    * the mod half is verified too. It lands outside the install dir and was checked by
      nothing at all -- a run that left the game with an EMPTY mod folder
      still wrote PASS (observed 2026-08-28, Setup 0.18.8).
    * the install dir is a CLOSED set. The manifest used to be a whitelist, so any file
      that no release ships -- a hand `dotnet publish` into the install
      directory, an assembly from an older layout -- sat there forever,
      invisible to the check and perfectly able to break assembly loading.
      That is the shape of the relay that could not cold-start in WO-69.
      Managed extensions (.dll .exe .pdb .deps.json .runtimeconfig.json) that
      are not in the manifest are now deleted and named in the report.

  Deliberately NOT swept: everything else. settings.json, favorites.json,
  custom_servers.json, the logs, and anything a user dropped in there are
  none of Setup's business, and an installer that eats user files to tidy up
  is a worse bug than the one being fixed. }

function PipeField(const S: String; Index: Integer): String;
var
  I, Start, Count: Integer;
begin
  Result := '';
  Start := 1;
  Count := 0;
  for I := 1 to Length(S) + 1 do
    if (I > Length(S)) or (S[I] = '|') then
    begin
      if Count = Index then
      begin
        Result := Copy(S, Start, I - Start);
        Exit;
      end;
      Count := Count + 1;
      Start := I + 1;
    end;
end;

{ '' on success, or why the manifest could not be read. }
function LoadManifest(): String;
var
  Lines: TArrayOfString;
  I, NA, NM: Integer;
  Line, Kind, Rel: String;
begin
  Result := '';
  SetArrayLength(ManApp, 0);
  SetArrayLength(ManMod, 0);
  AppIndex := '|';
  NA := 0;
  NM := 0;

  if not LoadStringsFromFile(ExpandConstant('{app}\install-manifest.txt'), Lines) then
  begin
    Result := 'install-manifest.txt is missing from the install folder';
    Exit;
  end;

  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Line := Trim(Lines[I]);
    if Line = '' then Continue;
    if Line[1] = '#' then Continue;
    Kind := PipeField(Line, 0);
    Rel := PipeField(Line, 1);
    if Rel = '' then Continue;
    if CompareText(Kind, 'APP') = 0 then
    begin
      NA := NA + 1;
      SetArrayLength(ManApp, NA);
      ManApp[NA - 1] := Rel + '|' + PipeField(Line, 2) + '|' + PipeField(Line, 3);
      AppIndex := AppIndex + Lowercase(Rel) + '|';
    end
    else if CompareText(Kind, 'MOD') = 0 then
    begin
      NM := NM + 1;
      SetArrayLength(ManMod, NM);
      ManMod[NM - 1] := Rel + '|' + PipeField(Line, 2) + '|' + PipeField(Line, 3);
    end;
  end;

  if NA = 0 then
    Result := 'install-manifest.txt lists no files -- it is not a v2 manifest';
end;

function EndsWithText(const S, Suffix: String): Boolean;
begin
  Result := (Length(S) > Length(Suffix)) and
            (CompareText(Copy(S, Length(S) - Length(Suffix) + 1, Length(Suffix)), Suffix) = 0);
end;

{ Files whose presence can change what the .NET runtime loads. Nothing else
  is ever a sweep candidate. unins* is Inno's own and is not in the manifest
  by definition. }
function IsSweepCandidate(const Name: String): Boolean;
begin
  Result := False;
  if CompareText(Copy(Name, 1, 5), 'unins') = 0 then Exit;
  Result := EndsWithText(Name, '.dll') or
            EndsWithText(Name, '.exe') or
            EndsWithText(Name, '.pdb') or
            EndsWithText(Name, '.deps.json') or
            EndsWithText(Name, '.runtimeconfig.json');
end;

procedure SweepDir(const Base, Rel: String; var Removed: TArrayOfString; var Count: Integer);
var
  FindRec: TFindRec;
  Dir, RelName, Full: String;
begin
  if Rel = '' then Dir := Base else Dir := Base + '\' + Rel;
  if not FindFirst(Dir + '\*', FindRec) then Exit;
  try
    repeat
      if (FindRec.Name = '.') or (FindRec.Name = '..') then Continue;
      if Rel = '' then RelName := FindRec.Name else RelName := Rel + '\' + FindRec.Name;
      Full := Base + '\' + RelName;

      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        SweepDir(Base, RelName, Removed, Count)
      else if IsSweepCandidate(FindRec.Name) then
        if Pos('|' + Lowercase(RelName) + '|', AppIndex) = 0 then
        begin
          ClearReadOnly(Full);
          if DeleteFile(Full) then
          begin
            Count := Count + 1;
            SetArrayLength(Removed, Count);
            Removed[Count - 1] := RelName;
            Log('repair: removed stale ' + RelName);
          end
          else
            Log('repair: could NOT remove stale ' + RelName);
        end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

{ True when the file matches; Detail describes the mismatch when it does not. }
function VerifyEntry(const Base, Entry: String; var Detail: String): Boolean;
var
  Rel, Full, WantHash, GotHash: String;
  WantSize, GotSize: Int64;
begin
  Result := False;
  Rel := PipeField(Entry, 0);
  WantSize := StrToInt64Def(PipeField(Entry, 1), -1);
  WantHash := Lowercase(PipeField(Entry, 2));
  Full := Base + '\' + Rel;

  if not FileExists(Full) then
  begin
    Detail := Rel + '  (missing)';
    Exit;
  end;

  GotSize := -1;
  if not FileSize64(Full, GotSize) then GotSize := -1;
  if GotSize <> WantSize then
  begin
    Detail := Rel + '  (' + IntToStr(GotSize) + ' bytes, expected ' + IntToStr(WantSize) + ')';
    Exit;
  end;

  { A file held open by something else throws here rather than returning a
    hash, and that is a failure worth naming, not one worth crashing on. }
  try
    GotHash := Lowercase(GetSHA256OfFile(Full));
  except
    Detail := Rel + '  (could not be read -- something is using it)';
    Exit;
  end;

  if GotHash <> WantHash then
  begin
    Detail := Rel + '  (wrong content -- sha256 ' + Copy(GotHash, 1, 12) +
              ', expected ' + Copy(WantHash, 1, 12) + ')';
    Exit;
  end;

  Result := True;
end;

{ Written before a single file is copied, so that a Setup which dies half way
  can never leave the PREVIOUS run's PASS sitting there as the record of what
  happened. Observed doing exactly that on 2026-08-28: an aborted 0.18.8
  upgrade left install-verify.txt reading PASS over an install it had just
  half-replaced and a game folder it had just emptied. }
procedure StampVerifyInProgress();
var
  Verdict: TArrayOfString;
begin
  ForceDirectories(ExpandConstant('{app}'));
  SetArrayLength(Verdict, 2);
  Verdict[0] := 'FAIL  install in progress -- Setup {#AppVersion} has not finished';
  Verdict[1] := '  If this line is still here, Setup stopped before it could verify anything.';
  SaveStringsToFile(ExpandConstant('{app}\install-verify.txt'), Verdict, False);
end;

procedure VerifyInstalledFiles();
var
  Failures, Removed, Verdict: TArrayOfString;
  I, FailCount, RemovedCount, Line: Integer;
  Detail, LoadError, ModDir: String;
begin
  VerifyFailed := False;
  FailCount := 0;
  RemovedCount := 0;
  SetArrayLength(Failures, 0);
  SetArrayLength(Removed, 0);

  LoadError := LoadManifest();
  if LoadError <> '' then
  begin
    { Every release since WO-74 ships one. Its absence IS a broken install. }
    FailCount := 1;
    SetArrayLength(Failures, 1);
    Failures[0] := LoadError;
    Log('verify FAIL: ' + LoadError);
  end
  else
  begin
    { Repair first, judge second -- a stale assembly that the sweep removes is
      not a failure, it is a fixed install. }
    SweepDir(ExpandConstant('{app}'), '', Removed, RemovedCount);

    for I := 0 to GetArrayLength(ManApp) - 1 do
      if not VerifyEntry(ExpandConstant('{app}'), ManApp[I], Detail) then
      begin
        FailCount := FailCount + 1;
        SetArrayLength(Failures, FailCount);
        Failures[FailCount - 1] := Detail;
        Log('verify FAIL: ' + Detail);
      end;

    { WO-150: a held-back mod is verified where it was put -- the staged copy
      in the app folder is an APP entry above; the game folder is the
      launcher's to fill, after its checklist links the workspace. }
    ModDir := GetKdcmpTargetDir('');
    if ShouldPlaceMod() then
    for I := 0 to GetArrayLength(ManMod) - 1 do
      if not VerifyEntry(ModDir, ManMod[I], Detail) then
      begin
        FailCount := FailCount + 1;
        SetArrayLength(Failures, FailCount);
        Failures[FailCount - 1] := 'game mod: ' + Detail;
        Log('verify FAIL: game mod: ' + Detail);
      end;
  end;

  { The verdict file is the record every tool reads: tools\Verify-Install.ps1,
    tools\Test-InstallerUpgrade.ps1, and anyone triaging a tester's machine. }
  SetArrayLength(Verdict, FailCount + RemovedCount + 3);
  Line := 0;
  if (FailCount = 0) and not ShouldPlaceMod() then
    Verdict[0] := 'PASS  ' + IntToStr(GetArrayLength(ManApp)) +
                  ' component(s) verified by sha256 against the install manifest; mod held back for the launcher'
  else if FailCount = 0 then
    Verdict[0] := 'PASS  ' + IntToStr(GetArrayLength(ManApp) + GetArrayLength(ManMod)) +
                  ' component(s) verified by sha256 against the install manifest'
  else
    Verdict[0] := 'FAIL  ' + IntToStr(FailCount) + ' component(s) did not install correctly:';
  Line := 1;
  for I := 0 to FailCount - 1 do
  begin
    Verdict[Line] := '  ' + Failures[I];
    Line := Line + 1;
  end;
  Verdict[Line] := 'version {#AppVersion}   repaired ' + IntToStr(RemovedCount) +
                   ' stale file(s) that no release ships';
  Line := Line + 1;
  for I := 0 to RemovedCount - 1 do
  begin
    Verdict[Line] := '  removed ' + Removed[I];
    Line := Line + 1;
  end;
  // WO-150: read by tools\Verify-Install.ps1, which then expects no mod in the game folder yet.
  if ShouldPlaceMod() then
    Verdict[Line] := 'mod placed by Setup'
  else
    Verdict[Line] := 'mod held back: the launcher places it once the Modding Tools are set up';
  SaveStringsToFile(ExpandConstant('{app}\install-verify.txt'), Verdict, False);

  if FailCount = 0 then
  begin
    Log('verify: PASS (' + IntToStr(GetArrayLength(ManApp) + GetArrayLength(ManMod)) +
        ' components, ' + IntToStr(RemovedCount) + ' stale removed)');
    Exit;
  end;

  { Loud, named, and non-zero. An installer that can end in a silent
    half-state is the bug class this work order exists to close, so a failure
    here is never allowed to look like success. }
  VerifyFailed := True;
  if not WizardSilent then
  begin
    Detail := '';
    for I := 0 to FailCount - 1 do
      if I < 8 then Detail := Detail + #13#10 + '    ' + Failures[I];
    if FailCount > 8 then
      Detail := Detail + #13#10 + '    ... and ' + IntToStr(FailCount - 8) + ' more';

    MsgBox('THIS INSTALL IS NOT COMPLETE.' + #13#10#13#10 +
           IntToStr(FailCount) + ' component(s) did not install correctly:' + Detail + #13#10#13#10 +
           'Almost always this means something was still using those files. Close the launcher, ' +
           'the agent, the relay, the master server and the game, then run this installer again -- ' +
           'it repairs an install in this state.' + #13#10#13#10 +
           'The full list is in install-verify.txt in the install folder.',
           mbCriticalError, MB_OK);
  end;
end;

// WO-148: the dice keys. Setup carries none of the game's files: the agent's
// --keys-pak verb reads the player's own Libs/Config/defaultProfile.xml and
// keybindSuperactions.xml out of the game's Data paks, adds our lines, and
// writes Mods\kdcmp\Data\kdcmp_keys.pak (dotnet\KcdMp.Client\KeybindPak.cs).
// Not part of the verdict: a failure costs the keys only (the console commands
// still work), and the launcher runs it again before every game start.
procedure BuildKeysPak();
var
  ResultCode: Integer;
  Agent, Params: String;
begin
  Agent := ExpandConstant('{app}\KcdMpClient.exe');
  if (not ShouldPlaceMod()) or not FileExists(Agent) then
  begin
    // WO-150: a held-back mod gets its keys from the launcher, after it places the mod.
    Log('keys pak: skipped (mod held back, or no agent)');
    Exit;
  end;
  Params := '--keys-pak --game-root "' + DetectedGameRoot + '" --mod-dir "' + GetKdcmpTargetDir('') + '"';
  if Exec(Agent, Params, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('keys pak: agent exit ' + IntToStr(ResultCode) + ' (0 = kdcmp_keys.pak in place)')
  else
    Log('keys pak: the agent could not be started (' + SysErrorMessage(ResultCode) + ')');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    StampVerifyInProgress()
  else if CurStep = ssPostInstall then
  begin
    VerifyInstalledFiles();
    if not VerifyFailed then BuildKeysPak();
    SeedSettings();
  end;
end;

{ Runs after the wizard is finished either way. See the ExitProcess comment. }
procedure DeinitializeSetup();
begin
  if VerifyFailed then
    ExitProcess(101);
end;

{ ----------------------------------------------------------- uninstalling }

// True if any of ours is still running. Inno's own CloseApplications drives
// the Restart Manager from the Files section, which does not cover the
// UninstallDelete sweep of the install directory -- so uninstalling with the
// launcher open removed the registry entries and the uninstaller itself but
// left ~120 files of ~680 behind. Observed on a real run, not theorised.
//
// tasklist rather than a mutex, because a mutex would mean changing the
// launcher and the uninstaller has no business requiring that. `find`
// returns exit code 1 when it matches nothing, which is the whole test.
function AnyOfOursRunning(): Boolean;
var
  I, ResultCode: Integer;
  Names: array[0..3] of String;
begin
  Result := False;
  Names[0] := 'KCDMP_launcher.exe';
  Names[1] := 'KcdMpClient.exe';
  Names[2] := 'KcdMpServer.exe';
  Names[3] := 'KcdMpMasterServer.exe';   { WO-74 -- see KillOursQuietly }

  for I := 0 to 3 do
    if Exec(ExpandConstant('{cmd}'),
            '/c tasklist /fi "imagename eq ' + Names[I] + '" /nh | find /i "' + Names[I] + '"',
            '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      if ResultCode = 0 then
      begin
        Result := True;
        Exit;
      end;
end;

{ Runs before anything is removed, and returning False aborts cleanly with
  nothing touched -- which is the point: a half-finished uninstall is worse
  than one that did not start. }
function InitializeUninstall(): Boolean;
begin
  Result := True;

  while AnyOfOursRunning() do
  begin
    if UninstallSilent then
    begin
      { Nobody to ask. Unattended means unattended, and the launcher only
        writes settings.json when the user presses Save, so there is no
        in-flight state to lose. }
      KillOursQuietly();
      Sleep(1500);
      if AnyOfOursRunning() then
      begin
        Result := False;
        Exit;
      end;
      Break;
    end;

    if MsgBox('Kingdom Come: Together is still running.' + #13#10#13#10 +
              'Close the launcher (and the game, if it is open) first, then click Retry.' + #13#10 +
              'Uninstalling now would leave files behind that Windows will not let it delete.',
              mbConfirmation, MB_RETRYCANCEL) <> IDRETRY then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Mods: String;
begin
  // Belt and braces for the case InitializeUninstall cannot catch: something
  // grabbed a file mid-uninstall. Better a plain sentence than silent debris
  // in the user's profile. Tested on the launcher exe rather than on the
  // install directory, which legitimately still holds unins000.exe here.
  if CurUninstallStep = usPostUninstall then
  begin
    if FileExists(ExpandConstant('{app}\KCDMP_launcher.exe')) and (not UninstallSilent) then
      MsgBox('Some files were in use and could not be removed. They are still in:' + #13#10#13#10 +
             ExpandConstant('{app}') + #13#10#13#10 +
             'Nothing there is needed any more -- deleting that folder by hand finishes the job.',
             mbInformation, MB_OK);
    Exit;
  end;

  if CurUninstallStep <> usUninstall then Exit;

  { The mod lives inside the game's own folder, so it is never removed
    silently -- and never without asking, unless this is an unattended run. }
  if not RegQueryStringValue(HKCU, 'Software\KCDMP', 'ModsPath', Mods) then Exit;
  if (Mods = '') or (not DirExists(Mods)) then Exit;

  if UninstallSilent then Exit;

  if MsgBox('Also remove the multiplayer mod from your game?' + #13#10#13#10 + Mods + #13#10#13#10 +
            'Choosing No leaves the mod installed; the game itself is not touched either way.',
            mbConfirmation, MB_YESNO) = IDYES then
    RemoveModFolder(Mods);
end;
