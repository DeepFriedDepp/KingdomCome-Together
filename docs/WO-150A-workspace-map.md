# WO-150A — the workspace map

What Warhorse's **Workspace Setup** (`<MT>\Tools\ModdingWorkspaceSetup\WorkspaceSetup.exe`)
produced on the maintainer's machine, read **read-only** on 2026-10-02. `<GAME>` is the
game's install folder (app 1771300, `steamapps\common\KingdomComeDeliverance2`), `<MT>` the
Modding Tools' (app 2429020, `steamapps\common\KCD2Mod`). Both are in the same Steam library
on the same NTFS volume here.

## The answer

- **91 files, all `*.pak`, all full copies**: no symlink, no hard link, no junction. Every
  entry has a link count of 1 and no reparse point, and every one has the game file's exact
  size **and** modified time (`File.Copy` keeps the time). Together they are **83.6 GB** of
  duplicate data.
- They were all created on 2026-08-26 between 13:09:40 and 13:11:29, one run of the tool.
  So on this machine the tool was answered **`C`** (copy), not `S`. Developer Mode is off and
  the shell was not elevated, so `S` could not have worked unelevated here
  (see `docs/WO-150-findings.md`, "Which link kind").
- **Nothing else** in `<MT>` comes from the tool. `<MT>\Data` and `<MT>\Localization` hold only
  these paks (no extra files beside them), and `<MT>\Data\Levels\<level>` holds only the
  level paks. The tool's code (read, not run) touches exactly three places: every `*.pak`
  directly in `<GAME>\Data`, every `*.pak` directly in `<GAME>\Localization`, and every
  `*.pak` directly in each `<GAME>\Data\Levels\<level>`, mirrored to the same relative path
  under `<MT>`. It does not recurse further, and it never deletes anything except a file it is
  about to replace.
- A fresh Modding Tools install has **no `<MT>\Data` folder at all** until this runs. The
  pre-WO-150 installer found the install root by looking for `Data` + `Engine`, which is why
  it could not see an unlinked Modding Tools (fixed: the root is now the folder above `Bin`).

`KcdMp.Setup.Workspace.ExpectedFiles` reproduces this list from `<GAME>`, and
`Workspace.Verify` reads the same 91 entries as **91 copies, all in place** on this machine
(`KcdMpSetup.exe check`, 36–56 ms).

## The 91 entries

`kind`: `copy` = a separate file (no reparse point, link count 1). `same as game` = identical
size and modified time to `<GAME>\<path>`, the check the launcher uses to accept a copy.

| `<MT>\` path (= `<GAME>\` path) | kind | bytes | links | same as game |
|---|---|---:|---:|---|
| `Data\Animations.pak` | copy | 820,904,851 | 1 | yes |
| `Data\Characters.pak` | copy | 1,346,031,222 | 1 | yes |
| `Data\Cinematics.pak` | copy | 1,167,038,769 | 1 | yes |
| `Data\GeomCaches.pak` | copy | 10,553,477 | 1 | yes |
| `Data\Heads.pak` | copy | 1,150,558,924 | 1 | yes |
| `Data\hlod_prefab.pak` | copy | 634,508,290 | 1 | yes |
| `Data\IPL_Characters-part0.pak` | copy | 1,995,415,068 | 1 | yes |
| `Data\IPL_Characters-part1.pak` | copy | 1,995,390,508 | 1 | yes |
| `Data\IPL_Characters-part2.pak` | copy | 1,995,506,556 | 1 | yes |
| `Data\IPL_Characters-part3.pak` | copy | 434,084,720 | 1 | yes |
| `Data\IPL_GameData.pak` | copy | 1,021,116,311 | 1 | yes |
| `Data\IPL_GeomCaches.pak` | copy | 4,773,238 | 1 | yes |
| `Data\IPL_Heads-part0.pak` | copy | 1,997,639,468 | 1 | yes |
| `Data\IPL_Heads-part1.pak` | copy | 40,929,054 | 1 | yes |
| `Data\IPL_Objects-part0.pak` | copy | 1,996,741,529 | 1 | yes |
| `Data\IPL_Objects-part1.pak` | copy | 1,995,632,489 | 1 | yes |
| `Data\IPL_Objects-part2.pak` | copy | 1,996,734,228 | 1 | yes |
| `Data\IPL_Objects-part3.pak` | copy | 1,996,756,857 | 1 | yes |
| `Data\IPL_Objects-part4.pak` | copy | 1,991,005,627 | 1 | yes |
| `Data\IPL_Objects-part5.pak` | copy | 1,595,037,100 | 1 | yes |
| `Data\IPL_Textures-part0.pak` | copy | 1,955,092,680 | 1 | yes |
| `Data\IPL_Textures-part1.pak` | copy | 1,995,842,525 | 1 | yes |
| `Data\IPL_Textures-part2.pak` | copy | 620,736,635 | 1 | yes |
| `Data\IPL_Videos-part0.pak` | copy | 1,529,370,554 | 1 | yes |
| `Data\IPL_Videos-part1.pak` | copy | 1,988,791,098 | 1 | yes |
| `Data\IPL_Videos-part2.pak` | copy | 1,584,993,022 | 1 | yes |
| `Data\Music.pak` | copy | 1,183,620,932 | 1 | yes |
| `Data\Objects-part0.pak` | copy | 1,996,534,389 | 1 | yes |
| `Data\Objects-part1.pak` | copy | 1,996,499,163 | 1 | yes |
| `Data\Objects-part2.pak` | copy | 1,994,702,041 | 1 | yes |
| `Data\Objects-part3.pak` | copy | 407,378,009 | 1 | yes |
| `Data\Scripts.pak` | copy | 79,397,175 | 1 | yes |
| `Data\Sounds.pak` | copy | 940,778,088 | 1 | yes |
| `Data\Tables.pak` | copy | 7,211,991 | 1 | yes |
| `Data\Textures-part0.pak` | copy | 2,003,148,634 | 1 | yes |
| `Data\Textures-part1.pak` | copy | 1,819,625,643 | 1 | yes |
| `Data\Videos-part0.pak` | copy | 1,499,982,505 | 1 | yes |
| `Data\Videos-part1.pak` | copy | 1,845,654,274 | 1 | yes |
| `Data\Videos-part2.pak` | copy | 1,435,053,434 | 1 | yes |
| `Data\Videos-part3.pak` | copy | 1,988,559,692 | 1 | yes |
| `Data\Videos-part4.pak` | copy | 1,824,507,570 | 1 | yes |
| `Data\Videos-part5.pak` | copy | 759,037,682 | 1 | yes |
| `Localization\Chineses_xml.pak` | copy | 12,779,044 | 1 | yes |
| `Localization\Chineset_xml.pak` | copy | 12,885,719 | 1 | yes |
| `Localization\Czech_xml.pak` | copy | 8,541,649 | 1 | yes |
| `Localization\English_xml.pak` | copy | 8,443,961 | 1 | yes |
| `Localization\english-part0.pak` | copy | 1,990,848,456 | 1 | yes |
| `Localization\english-part1.pak` | copy | 1,586,589,447 | 1 | yes |
| `Localization\english-part2.pak` | copy | 1,518,094,173 | 1 | yes |
| `Localization\english-part3.pak` | copy | 1,784,862,151 | 1 | yes |
| `Localization\French_xml.pak` | copy | 12,110,925 | 1 | yes |
| `Localization\German_xml.pak` | copy | 12,228,233 | 1 | yes |
| `Localization\IPL_english.pak` | copy | 783,448,294 | 1 | yes |
| `Localization\Italian_xml.pak` | copy | 11,714,183 | 1 | yes |
| `Localization\Japanese_xml.pak` | copy | 12,840,326 | 1 | yes |
| `Localization\Korean_xml.pak` | copy | 13,013,703 | 1 | yes |
| `Localization\Polish_xml.pak` | copy | 12,072,992 | 1 | yes |
| `Localization\Portuguese_xml.pak` | copy | 11,754,050 | 1 | yes |
| `Localization\Russian_xml.pak` | copy | 13,375,622 | 1 | yes |
| `Localization\Spanish_xml.pak` | copy | 12,067,712 | 1 | yes |
| `Localization\Turkish_xml.pak` | copy | 12,061,077 | 1 | yes |
| `Localization\Ukrainian_xml.pak` | copy | 13,380,984 | 1 | yes |
| `Localization\Vietnamese_xml.pak` | copy | 12,570,575 | 1 | yes |
| `Data\Levels\klaster\cestool.pak` | copy | 246,470,043 | 1 | yes |
| `Data\Levels\klaster\hlod.pak` | copy | 159,673,988 | 1 | yes |
| `Data\Levels\klaster\hlod_vegetation.pak` | copy | 8,435,527 | 1 | yes |
| `Data\Levels\klaster\level.pak` | copy | 140,659,429 | 1 | yes |
| `Data\Levels\klaster\recast.pak` | copy | 5,919,193 | 1 | yes |
| `Data\Levels\klaster\svo.pak` | copy | 1,749,403,537 | 1 | yes |
| `Data\Levels\klaster\terrain.pak` | copy | 105,082,057 | 1 | yes |
| `Data\Levels\kutnohorsko\cestool.pak` | copy | 1,174,422,240 | 1 | yes |
| `Data\Levels\kutnohorsko\hlod.pak` | copy | 903,322,734 | 1 | yes |
| `Data\Levels\kutnohorsko\hlod_vegetation.pak` | copy | 166,377,196 | 1 | yes |
| `Data\Levels\kutnohorsko\IPL_svo.pak` | copy | 1,552,742,765 | 1 | yes |
| `Data\Levels\kutnohorsko\level.pak` | copy | 794,349,942 | 1 | yes |
| `Data\Levels\kutnohorsko\recast.pak` | copy | 99,732,010 | 1 | yes |
| `Data\Levels\kutnohorsko\svo-part0.pak` | copy | 1,975,391,046 | 1 | yes |
| `Data\Levels\kutnohorsko\svo-part1.pak` | copy | 1,970,025,137 | 1 | yes |
| `Data\Levels\kutnohorsko\svo-part2.pak` | copy | 1,961,390,406 | 1 | yes |
| `Data\Levels\kutnohorsko\terrain.pak` | copy | 672,658,814 | 1 | yes |
| `Data\Levels\trosecko\cestool.pak` | copy | 419,255,482 | 1 | yes |
| `Data\Levels\trosecko\hlod.pak` | copy | 444,503,843 | 1 | yes |
| `Data\Levels\trosecko\hlod_vegetation.pak` | copy | 238,695,640 | 1 | yes |
| `Data\Levels\trosecko\IPL_svo.pak` | copy | 1,820,205,455 | 1 | yes |
| `Data\Levels\trosecko\level.pak` | copy | 700,144,191 | 1 | yes |
| `Data\Levels\trosecko\recast.pak` | copy | 67,509,960 | 1 | yes |
| `Data\Levels\trosecko\svo-part0.pak` | copy | 1,970,765,984 | 1 | yes |
| `Data\Levels\trosecko\svo-part1.pak` | copy | 1,996,663,954 | 1 | yes |
| `Data\Levels\trosecko\svo-part2.pak` | copy | 1,986,629,247 | 1 | yes |
| `Data\Levels\trosecko\svo-part3.pak` | copy | 644,303,216 | 1 | yes |
| `Data\Levels\trosecko\terrain.pak` | copy | 278,562,237 | 1 | yes |

## Folders in `<MT>`, for completeness

`Bin`, `BugSplatAttachments`, `ConsoleHTMLHelp`, `Data`, `Editor`, `Engine`, `Localization`,
`logbackups`, `Mods`, `outputs`, `Tools`. Of these only `Data` and `Localization` (and the
`Data\Levels\<level>` folders under `Data`) are written by the workspace tool.
