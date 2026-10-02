// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// Steam / game / Modding Tools / workspace detection for the installer.
//
// WO-150: there is no detection code here any more. Setup asks the same code
// the launcher runs (dotnet\KcdMp.Setup), through KcdMpSetup.exe -- a small
// NativeAOT build of it that needs no .NET runtime, extracted to {tmp} before
// anything is installed. So the installer and the launcher can never disagree
// about where Steam, the game and the Modding Tools are, or whether the
// workspace is linked. This file only runs the helper and reads its answer
// (key=value lines, KcdMp.Setup.DetectReport).
//
// #include this from inside a [Code] section, in a script whose [Files] carries
//   Source: "<...>\KcdMpSetup.exe"; Flags: dontcopy
// tests\SteamDetectProbe.iss includes it exactly as KCDMP.iss does.

// Steam application ID of "Kingdom Come: Deliverance II Modding tools" (installdir
// "KCD2Mod"); retail KCD2 is 1771300 and cannot run this mod -- see docs/LAUNCHING.md.
#define ModdingToolsAppId "2429020"

var
  DetectKeys: TArrayOfString;
  DetectValues: TArrayOfString;

function BackslashPath(const S: String): String;
begin
  Result := S;
  StringChangeEx(Result, '/', '\', True);
  while (Length(Result) > 0) and (Result[Length(Result)] = '\') do
    Result := Copy(Result, 1, Length(Result) - 1);
end;

{ The helper, extracted once per Setup run. '' when it cannot be had. }
function SetupHelperPath(): String;
begin
  Result := ExpandConstant('{tmp}\KcdMpSetup.exe');
  if not FileExists(Result) then
  try
    ExtractTemporaryFile('KcdMpSetup.exe');
  except
    Log('detect: the setup helper could not be extracted: ' + GetExceptionMessage);
  end;
  if not FileExists(Result) then Result := '';
end;

procedure ClearDetect();
begin
  SetArrayLength(DetectKeys, 0);
  SetArrayLength(DetectValues, 0);
end;

{ The value the last detection reported for Key, or ''. }
function DetectValue(const Key: String): String;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to GetArrayLength(DetectKeys) - 1 do
    if CompareText(DetectKeys[I], Key) = 0 then
    begin
      Result := DetectValues[I];
      Exit;
    end;
end;

function LoadKeyValues(const FileName: String): Boolean;
var
  Lines: TArrayOfString;
  I, N, Eq: Integer;
begin
  ClearDetect();
  Result := LoadStringsFromFile(FileName, Lines);
  if not Result then Exit;
  N := 0;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Eq := Pos('=', Lines[I]);
    if Eq > 1 then
    begin
      N := N + 1;
      SetArrayLength(DetectKeys, N);
      SetArrayLength(DetectValues, N);
      DetectKeys[N - 1] := Copy(Lines[I], 1, Eq - 1);
      DetectValues[N - 1] := Copy(Lines[I], Eq + 1, Length(Lines[I]));
    end;
  end;
end;

{ Runs "KcdMpSetup.exe detect". SteamRoot '' = the registry's Steam (a
  non-empty one that does not exist means "no Steam", as /STEAMROOT always
  has); MtExe '' = find the Modding Tools through Steam. False when the helper
  could not be run or said nothing: the caller then treats everything as
  unknown, installs the launcher and holds the mod back. }
function RunDetect(const SteamRoot, MtExe: String): Boolean;
var
  Helper, OutFile, Params: String;
  ResultCode: Integer;
begin
  Result := False;
  ClearDetect();
  Helper := SetupHelperPath();
  if Helper = '' then Exit;

  OutFile := ExpandConstant('{tmp}\kcdmp-detect.txt');
  DeleteFile(OutFile);
  Params := 'detect --out "' + OutFile + '"';
  if SteamRoot <> '' then Params := Params + ' --steam-root "' + BackslashPath(SteamRoot) + '"';
  if MtExe <> '' then Params := Params + ' --mt-exe "' + MtExe + '"';

  if not Exec(Helper, Params, ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log('detect: the setup helper could not be started (' + SysErrorMessage(ResultCode) + ')');
    Exit;
  end;
  Result := LoadKeyValues(OutFile) and (DetectValue('helper') = '1');
  if Result then
    Log('detect: steam=' + DetectValue('steam_found') + ' game=' + DetectValue('game_found') +
        ' mt=' + DetectValue('mt_found') + ' workspace=' + DetectValue('workspace') +
        ' (' + DetectValue('workspace_ok') + '/' + DetectValue('workspace_expected') + ')' +
        ' place_mod=' + DetectValue('place_mod'))
  else
    Log('detect: the setup helper gave no answer (exit ' + IntToStr(ResultCode) + ')');
end;

{ Setup's Browse button: is this a Modding Tools KingdomCome.exe, and where is its root. }
function CheckModdingToolsExe(const ExePath: String; var Root: String): Boolean;
var
  Helper, OutFile: String;
  ResultCode: Integer;
begin
  Result := False;
  Root := '';
  Helper := SetupHelperPath();
  if Helper = '' then Exit;
  OutFile := ExpandConstant('{tmp}\kcdmp-checkexe.txt');
  DeleteFile(OutFile);
  if not Exec(Helper, 'check-exe --exe "' + ExePath + '" --out "' + OutFile + '"', ExpandConstant('{tmp}'),
              SW_HIDE, ewWaitUntilTerminated, ResultCode) then Exit;
  if not LoadKeyValues(OutFile) then Exit;
  Root := DetectValue('mt_root');
  Result := (DetectValue('mt_build') = '1') and (Root <> '');
end;
