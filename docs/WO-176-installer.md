# WO-176 — the 0.50.0 installer record (local, unsigned, release candidate)

**Second build (current): `mp_real_hits` on, the maintainer's request (2026-10-12).** It was built unattended 2026-10-12 08:15–08:20
from a fresh clone of `b7ab169` (`release\c0500b`, git-ignored). The three start saves were copied in, SHA-1 equal to the source copies.
The Modding Tools game was running with the throwaway `playline4/autosave360` loaded, so the live save round trip ran:
`tools\Build-Installer.ps1 -SoakWaiver "the maintainer's standing rule, quoted in the WO-176 run prompt (2026-10-11): 'Ends in a
release-candidate installer, always (soak waived under the standing rule, quoted and dated)'; second 0.50.0 build (mp_real_hits on, the
maintainer's request) 2026-10-12"`. Exit 0.

| | |
|---|---|
| file | `release\KingdomComeTogether-Setup-0.50.0.exe` (local only; nothing uploaded) |
| size | 106,956,024 bytes |
| sha256 | `85f535331afb12774b0d56026e91f45ad1ece6e94926c03b9319eecf36ff0e0e` |
| header version | **0.50.0** (checked: the compile line `KCDMP.iss (version 0.50.0)` and the exe's ProductVersion / FileVersion) |
| signed | no (no signing settings on this machine) |
| soak | waived, as quoted above (`release\SOAK-WAIVED-0.50.0.txt`); the same-machine frame A/B against 0.49.2 is in the findings |
| defaults | `mp_arranged_guard` on, `mp_copy_blocks` on, `mp_block_recoil` on, **`mp_real_hits` on**, `mp_victim_decides` off |

The second build's first attempt (07:50, the same commit) stopped at the live save round trip. The start save was `autosave922`: draw
DRAWN → resting save → reload → draw REFUSED. The same failure was reproduced on fresh launches with the first build's pak, and with
**the game without the mod**, so it comes from that save, not from 0.50.0 (findings, "Incidents"). The build was redone on `autosave360`,
the start save the first build used. The failed attempt's logs are `release\BUILD-0.50.0.second-build-gatefail.*`.

**First build (superseded): `mp_real_hits` off.** It was built from a fresh clone of `f00aef7` (`release\c0500`), on `autosave360`.
The installer is kept as `release\KingdomComeTogether-Setup-0.50.0.superseded-first-build.exe`: 106,949,330 bytes, sha256
`9e4ee30fc730d3b6093a8b8f2f2f1766949c62920762596fa6e7225541b1c021`; its log is `release\BUILD-0.50.0.first-build.log`. Before that,
an attempt from `85fed25` stopped at the Lua gate: `Test-WO175Synthetic` L7 counted one timer after a load, but since 0.50.0 the same
hook also re-arms WO-176's figure check (an intended change). The check was updated (`f00aef7`).

## The build's own gates

| gate | 0.49.2 | 0.50.0 (second build) |
|---|---|---|
| relay | 63 | **63** |
| agent (`KcdMp.Client.Tests`) | 1,503 | **1,512** (+9: `Wo176Tests`) |
| setup | 77 | **77** |
| log bundle | 14 | **14** |
| Lua synthetic suites | 60 / 4,017 checks | **61 / 4,049 checks**, 0 failed (new `Test-WO176Synthetic`, 32) |
| static checks (console placeholders, Lua locals, native guards 7/7, WO-157) | green | **green** |
| save tools (`test_whs_repair.py`, `test_whs_dump.py`) | 14 + 7 | **14 + 7** |
| save round trip, live (WO-175 S5) | SKIPPED (no game) | **PASS** (the game up, `autosave360`: draw → save → health → reload → draw) |
| native | 9,318 | **9,364** (+46: `wo176_tests`) |
| payload smoke | ok | **ok** (`RELAY-SMOKE ok protocol=v10 release=0.50.0`) |
| no-input-tooling (WO-175) | 0 hits | **0 hits** over 1,036 payload files (the Microsoft .NET Desktop runtime exemptions unchanged, listed by name) |
| installer detection / the four cases | 26/26, 41/41 | **26/26, 41/41** |
