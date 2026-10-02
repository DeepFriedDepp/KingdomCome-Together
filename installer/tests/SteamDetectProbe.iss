; Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
; GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
; content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
; Test harness for installer\SteamDetect.iss.
;
; Compiles the installer's real detection wrapper -- the same file KCDMP.iss
; includes, not a copy -- with the same setup helper (KcdMpSetup.exe, WO-150)
; into a tiny setup that runs detection against a fixture tree and prints one
; RESULT line per case, then aborts before installing anything.
;
; Driven by tools\Test-InstallerDetect.ps1, which builds the fixtures and the
; helper, compiles this with /DHelperExe=<path>, runs it, and asserts on the
; output. Not meant to be run by hand.
;
;   SteamDetectProbe.exe /VERYSILENT /SUPPRESSMSGBOXES /LOG=<log> /FIXTURES=<dir>

#ifndef HelperExe
  #define HelperExe "..\..\release\KCDMP\KcdMpSetup.exe"
#endif

[Setup]
AppName=KCDMP SteamDetect probe
AppVersion=1.0
DefaultDirName={localappdata}\KCDMPSteamDetectProbe
PrivilegesRequired=lowest
OutputDir=.
OutputBaseFilename=SteamDetectProbe
Uninstallable=no
CreateAppDir=no
SetupLogging=yes

[Files]
Source: "{#HelperExe}"; Flags: dontcopy

[Code]
#include "..\SteamDetect.iss"

procedure RunCase(const Name, SteamPath, MtExe: String);
var
  Ok: Boolean;
begin
  Ok := RunDetect(SteamPath, MtExe);
  Log('RESULT ' + Name +
      ' | ran=' + IntToStr(Integer(Ok)) +
      ' | steam=' + DetectValue('steam_found') +
      ' | libs=' + DetectValue('libraries') +
      ' | game=' + DetectValue('game_found') +
      ' | registered=' + DetectValue('mt_registered') +
      ' | found=' + DetectValue('mt_found') +
      ' | exe=' + DetectValue('mt_exe') +
      ' | root=' + DetectValue('mt_root') +
      ' | workspace=' + DetectValue('workspace') +
      ' | place_mod=' + DetectValue('place_mod'));
end;

function InitializeSetup(): Boolean;
var
  Fixtures, Root: String;
begin
  Fixtures := ExpandConstant('{param:fixtures}');

  { Detection cases, carried over from before WO-150. }
  RunCase('multi-library', Fixtures + '\multi\Steam', '');
  RunCase('app-in-root-library', Fixtures + '\rootlib\Steam', '');
  RunCase('missing-app', Fixtures + '\missing\Steam', '');
  RunCase('malformed-vdf', Fixtures + '\malformed\Steam', '');
  RunCase('no-vdf-at-all', Fixtures + '\novdf\Steam', '');
  RunCase('manifest-but-no-files', Fixtures + '\ghost\Steam', '');
  RunCase('retail-not-modding-tools', Fixtures + '\retail\Steam', '');
  RunCase('offline-library', Fixtures + '\offline\Steam', '');
  RunCase('no-steam', Fixtures + '\no\such\steam', '');

  { WO-150 Part 3: the four cases the installer and the launcher must agree on. }
  RunCase('case1-nothing', Fixtures + '\case1\Steam', '');
  RunCase('case2-mt-unlinked', Fixtures + '\case2\Steam', '');
  RunCase('case3-linked-no-mod', Fixtures + '\case3\Steam', '');
  RunCase('case4-everything', Fixtures + '\case4\Steam', '');

  { Browse: an explicit exe wins when it passes, retail is refused. }
  RunCase('browse-explicit', Fixtures + '\missing\Steam', Fixtures + '\multi\Lib2\steamapps\common\KCD2Mod\Bin\Win64ReleaseSteamLTO_DLL\KingdomCome.exe');
  Log('RESULT checkexe-mt | ok=' + IntToStr(Integer(CheckModdingToolsExe(Fixtures + '\multi\Lib2\steamapps\common\KCD2Mod\Bin\Win64ReleaseSteamLTO_DLL\KingdomCome.exe', Root))) + ' | root=' + Root);
  Log('RESULT checkexe-retail | ok=' + IntToStr(Integer(CheckModdingToolsExe(Fixtures + '\retail\Steam\steamapps\common\KCD2Mod\Bin\Win64MasterMasterSteamPGO\KingdomCome.exe', Root))) + ' | root=' + Root);

  { The real Steam install on the machine running this (read only). }
  RunCase('real-machine', '', '');

  Result := False;
end;
